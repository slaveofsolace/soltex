using System.Buffers.Binary;

namespace Soltex.Security;

internal sealed record ZipCentralDirectorySummary(int EntryCount);

internal static class ZipCentralDirectoryPreflight
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064B50;
    private const uint CentralDirectoryFileHeaderSignature = 0x02014B50;
    private const ushort Zip64ExtraFieldId = 0x0001;
    private const int EndOfCentralDirectoryFixedLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const int MaximumTailLength = EndOfCentralDirectoryFixedLength + MaximumZipCommentLength;
    private const int Zip64LocatorLength = 20;
    private const int CentralDirectoryFixedHeaderLength = 46;

    internal static async Task<ZipCentralDirectorySummary> ValidateAsync(
        FileStream stream,
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new InvalidDataException("The ZIP archive stream must be readable and seekable.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);

        long archiveLength = stream.Length;
        if (archiveLength < EndOfCentralDirectoryFixedLength)
        {
            throw new InvalidDataException("The ZIP archive is truncated before its end record.");
        }

        try
        {
            int tailLength = checked((int)Math.Min(archiveLength, MaximumTailLength));
            byte[] tail = new byte[tailLength];
            stream.Position = archiveLength - tailLength;
            await ReadExactlyAsync(stream, tail, cancellationToken).ConfigureAwait(false);

            int endIndex = FindUniqueEndRecord(tail);
            ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(endIndex + 4, 2));
            ushort centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(endIndex + 6, 2));
            ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(endIndex + 8, 2));
            ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(endIndex + 10, 2));
            uint centralDirectoryLength32 = BinaryPrimitives.ReadUInt32LittleEndian(
                tail.AsSpan(endIndex + 12, 4));
            uint centralDirectoryOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(
                tail.AsSpan(endIndex + 16, 4));

            if (endIndex >= Zip64LocatorLength &&
                BinaryPrimitives.ReadUInt32LittleEndian(
                    tail.AsSpan(endIndex - Zip64LocatorLength, 4)) ==
                Zip64EndOfCentralDirectoryLocatorSignature)
            {
                throw new InvalidDataException(
                    "ZIP64 archives are not accepted by this bounded staging implementation.");
            }

            if (diskNumber != 0 ||
                centralDirectoryDisk != 0 ||
                entriesOnDisk != totalEntries)
            {
                throw new InvalidDataException("Multi-disk ZIP archives are not accepted.");
            }

            if (totalEntries == ushort.MaxValue ||
                centralDirectoryLength32 == uint.MaxValue ||
                centralDirectoryOffset32 == uint.MaxValue)
            {
                throw new InvalidDataException(
                    "ZIP64 archives are not accepted by this bounded staging implementation.");
            }

            int entryCount = totalEntries;
            if (entryCount is < 1 || entryCount > maximumEntries)
            {
                throw new InvalidDataException(
                    $"The ZIP archive must contain between 1 and {maximumEntries} entries.");
            }

            long centralDirectoryOffset = centralDirectoryOffset32;
            long centralDirectoryLength = centralDirectoryLength32;
            long endRecordOffset = archiveLength - tailLength + endIndex;
            long centralDirectoryEnd = checked(centralDirectoryOffset + centralDirectoryLength);
            if (centralDirectoryLength < checked((long)entryCount * CentralDirectoryFixedHeaderLength) ||
                centralDirectoryEnd != endRecordOffset ||
                centralDirectoryEnd > archiveLength)
            {
                throw new InvalidDataException(
                    "The ZIP central-directory range is inconsistent with the end record.");
            }

            await ValidateCentralDirectoryEntriesAsync(
                stream,
                centralDirectoryOffset,
                centralDirectoryEnd,
                entryCount,
                cancellationToken).ConfigureAwait(false);
            return new ZipCentralDirectorySummary(entryCount);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "The ZIP directory uses overflowing offsets or lengths.",
                exception);
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static int FindUniqueEndRecord(ReadOnlySpan<byte> tail)
    {
        int match = -1;
        for (int index = tail.Length - EndOfCentralDirectoryFixedLength; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail[index..]) !=
                EndOfCentralDirectorySignature)
            {
                continue;
            }

            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.Slice(index + 20, 2));
            if (index + EndOfCentralDirectoryFixedLength + commentLength != tail.Length)
            {
                continue;
            }

            if (match != -1)
            {
                throw new InvalidDataException("The ZIP archive contains ambiguous end records.");
            }

            match = index;
        }

        return match >= 0
            ? match
            : throw new InvalidDataException("The ZIP end-of-central-directory record is missing.");
    }

    private static async Task ValidateCentralDirectoryEntriesAsync(
        FileStream stream,
        long centralDirectoryOffset,
        long centralDirectoryEnd,
        int entryCount,
        CancellationToken cancellationToken)
    {
        stream.Position = centralDirectoryOffset;
        byte[] fixedHeader = new byte[CentralDirectoryFixedHeaderLength];
        long cursor = centralDirectoryOffset;
        for (int index = 0; index < entryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (checked(cursor + CentralDirectoryFixedHeaderLength) > centralDirectoryEnd)
            {
                throw new InvalidDataException("The ZIP central directory is truncated.");
            }

            await ReadExactlyAsync(stream, fixedHeader, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.AsSpan(0, 4)) !=
                CentralDirectoryFileHeaderSignature)
            {
                throw new InvalidDataException(
                    "The ZIP central directory contains an invalid header.");
            }

            uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(
                fixedHeader.AsSpan(20, 4));
            uint expandedLength = BinaryPrimitives.ReadUInt32LittleEndian(
                fixedHeader.AsSpan(24, 4));
            ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                fixedHeader.AsSpan(28, 2));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                fixedHeader.AsSpan(30, 2));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                fixedHeader.AsSpan(32, 2));
            ushort diskStart = BinaryPrimitives.ReadUInt16LittleEndian(
                fixedHeader.AsSpan(34, 2));
            uint localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                fixedHeader.AsSpan(42, 4));
            if (fileNameLength == 0)
            {
                throw new InvalidDataException("A ZIP central-directory entry has no filename.");
            }

            if (diskStart != 0)
            {
                throw new InvalidDataException("Multi-disk ZIP entries are not accepted.");
            }

            if (compressedLength == uint.MaxValue ||
                expandedLength == uint.MaxValue ||
                localHeaderOffset == uint.MaxValue)
            {
                throw new InvalidDataException(
                    "ZIP64 entry metadata is not accepted by this staging implementation.");
            }

            long variableLength = checked((long)fileNameLength + extraLength + commentLength);
            long recordEnd = checked(cursor + CentralDirectoryFixedHeaderLength + variableLength);
            if (recordEnd > centralDirectoryEnd)
            {
                throw new InvalidDataException(
                    "A ZIP central-directory entry extends beyond the declared directory.");
            }

            stream.Position = checked(stream.Position + fileNameLength);
            if (extraLength > 0)
            {
                byte[] extra = new byte[extraLength];
                await ReadExactlyAsync(stream, extra, cancellationToken).ConfigureAwait(false);
                RejectZip64ExtraFields(extra);
            }

            stream.Position = checked(stream.Position + commentLength);
            cursor = recordEnd;
        }

        if (cursor != centralDirectoryEnd || stream.Position != centralDirectoryEnd)
        {
            throw new InvalidDataException(
                "The ZIP central directory contains undeclared or trailing records.");
        }
    }

    private static void RejectZip64ExtraFields(ReadOnlySpan<byte> extra)
    {
        int offset = 0;
        while (offset < extra.Length)
        {
            if (extra.Length - offset < 4)
            {
                throw new InvalidDataException("A ZIP extra-field header is truncated.");
            }

            ushort identifier = BinaryPrimitives.ReadUInt16LittleEndian(extra[offset..]);
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(offset + 2)..]);
            offset = checked(offset + 4);
            if (offset + length > extra.Length)
            {
                throw new InvalidDataException("A ZIP extra field extends beyond its entry.");
            }

            if (identifier == Zip64ExtraFieldId)
            {
                throw new InvalidDataException(
                    "ZIP64 entry metadata is not accepted by this staging implementation.");
            }

            offset = checked(offset + length);
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = await stream.ReadAsync(destination[offset..], cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("The ZIP archive ended unexpectedly.");
            }

            offset += read;
        }
    }
}
