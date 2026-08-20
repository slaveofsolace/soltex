using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Soltex.Whisper.WinUiTarget;

public sealed partial class MainWindow : Window
{
    private int _enterReceiptCount;

    public MainWindow()
    {
        InitializeComponent();
        TargetTextBox.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(TargetTextBox_KeyDown),
            handledEventsToo: true);
        TargetTextBox.Loaded += (_, _) =>
            TargetTextBox.Focus(FocusState.Programmatic);
    }

    private void TargetTextBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        _enterReceiptCount++;
        EnterReceiptStatus.Text = $"Enter receipts: {_enterReceiptCount}";
    }
}
