using System.Xml;
using System.Xml.Linq;

namespace Soltex.Security;

public static class DefenderEventLogParser
{
    public static DefenderOperationalEvent Parse(
        int eventId,
        long recordId,
        DateTimeOffset timestampUtc,
        string eventXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventXml);

        Dictionary<string, string> fields;
        try
        {
            XDocument document = XDocument.Parse(eventXml, LoadOptions.None);
            fields = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Data", StringComparison.Ordinal))
                .Select(element => new
                {
                    Name = element.Attribute("Name")?.Value,
                    Value = element.Value
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Value,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The Defender event XML was malformed.", exception);
        }

        DefenderEventKind kind = KindFor(eventId);
        SecurityEventSeverity severity = SeverityFor(eventId, kind);
        string title = TitleFor(eventId, kind);
        string detail = DetailFor(kind, fields);
        bool resourcePathRedacted = fields.Any(pair =>
            !string.IsNullOrWhiteSpace(pair.Value) &&
            (pair.Key.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
             pair.Key.Equals("Process Name", StringComparison.OrdinalIgnoreCase) ||
             pair.Key.Equals("Scan Resources", StringComparison.OrdinalIgnoreCase)));

        return new DefenderOperationalEvent(
            recordId,
            eventId,
            timestampUtc.ToUniversalTime(),
            kind,
            severity,
            title,
            detail,
            resourcePathRedacted);
    }

    private static DefenderEventKind KindFor(int eventId) => eventId switch
    {
        1000 => DefenderEventKind.ScanStarted,
        1001 => DefenderEventKind.ScanCompleted,
        1002 => DefenderEventKind.ScanCancelled,
        1006 or 1015 or 1116 => DefenderEventKind.Detection,
        1007 or 1117 => DefenderEventKind.Remediation,
        1008 or 1118 or 1119 => DefenderEventKind.RemediationFailed,
        3002 or 5008 => DefenderEventKind.ProtectionFailure,
        3007 => DefenderEventKind.ProtectionRecovered,
        5000 => DefenderEventKind.ProtectionEnabled,
        5001 or 5012 => DefenderEventKind.ProtectionDisabled,
        5007 => DefenderEventKind.ConfigurationChanged,
        5013 => DefenderEventKind.TamperBlocked,
        _ => DefenderEventKind.Other
    };

    private static SecurityEventSeverity SeverityFor(int eventId, DefenderEventKind kind) =>
        eventId is 1119 or 5008 or 5012
            ? SecurityEventSeverity.Critical
            : kind switch
            {
                DefenderEventKind.Detection => SecurityEventSeverity.Critical,
                DefenderEventKind.ScanCancelled or
                DefenderEventKind.RemediationFailed or
                DefenderEventKind.ProtectionDisabled or
                DefenderEventKind.ProtectionFailure or
                DefenderEventKind.ConfigurationChanged or
                DefenderEventKind.TamperBlocked => SecurityEventSeverity.Warning,
                _ => SecurityEventSeverity.Information
            };

    private static string TitleFor(int eventId, DefenderEventKind kind) => eventId switch
    {
        1006 or 1116 => "Threat detected",
        1015 => "Suspicious behavior detected",
        1007 or 1117 => "Threat action completed",
        1008 or 1118 => "Threat action incomplete",
        1119 => "Threat action critically failed",
        3002 => "Real-time protection failed",
        3007 => "Real-time protection recovered",
        5000 => "Real-time protection enabled",
        5001 => "Real-time protection disabled",
        5007 => "Protection configuration changed",
        5008 => "Antimalware engine failed",
        5012 => "Antivirus scanning disabled",
        5013 => "Tamper protection blocked a change",
        _ => kind switch
        {
            DefenderEventKind.ScanStarted => "Scan started",
            DefenderEventKind.ScanCompleted => "Scan completed",
            DefenderEventKind.ScanCancelled => "Scan cancelled",
            _ => $"Defender event {eventId}"
        }
    };

    private static string DetailFor(
        DefenderEventKind kind,
        IReadOnlyDictionary<string, string> fields)
    {
        string threat = Get(fields, "Threat Name");
        string severity = Get(fields, "Severity Name");
        string category = Get(fields, "Category Name");
        string source = Get(fields, "Source Name");
        string action = Get(fields, "Action Name");
        string scanType = Get(fields, "Scan Type");
        string scanParameters = Get(fields, "Scan Parameters");
        string feature = Get(fields, "Feature");
        string errorCode = Get(fields, "Error Code");
        string failureType = Get(fields, "Failure Type");

        string[] parts = kind switch
        {
            DefenderEventKind.Detection => [threat, severity, category, source],
            DefenderEventKind.Remediation => [threat, action, severity],
            DefenderEventKind.RemediationFailed => [threat, action, errorCode],
            DefenderEventKind.ScanStarted or
            DefenderEventKind.ScanCompleted or
            DefenderEventKind.ScanCancelled => [scanType, scanParameters],
            DefenderEventKind.ProtectionFailure => [feature, failureType, errorCode],
            DefenderEventKind.ConfigurationChanged =>
                ["Review the change in Windows Security if it was not expected."],
            DefenderEventKind.TamperBlocked =>
                ["Windows rejected a Defender setting change while tamper protection was active."],
            DefenderEventKind.ProtectionRecovered =>
                ["Windows reports that the real-time protection component restarted."],
            DefenderEventKind.ProtectionEnabled => ["Windows reports that real-time protection is enabled."],
            DefenderEventKind.ProtectionDisabled => ["Windows reports that a protection component is disabled."],
            _ => ["Recorded by the Microsoft Defender Operational event log."]
        };

        string detail = string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(detail)
            ? "Recorded by the Microsoft Defender Operational event log."
            : Sanitize(detail, 360);
    }

    private static string Get(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value) ? Sanitize(value, 120) : string.Empty;

    private static string Sanitize(string value, int maximum)
    {
        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= maximum ? sanitized : sanitized[..maximum];
    }
}
