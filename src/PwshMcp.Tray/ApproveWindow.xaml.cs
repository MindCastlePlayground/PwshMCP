using System.Windows;

namespace PwshMcp.Tray;

public partial class ApproveWindow : Window
{
    readonly ApproveVm vm;

    public ApproveWindow(ApproveVm vm)
    {
        this.vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.Finished += Close;
        // Focus starts on Cancel: Enter never approves by accident.
        Loaded += async (_, _) => { cancel.Focus(); await vm.Load(); };
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // An approve in flight must finish so the result is known; closing mid-send could hide an Active grant.
        if (vm.Step is Step.Sending or Step.Starting) e.Cancel = true;
        base.OnClosing(e);
    }
}
