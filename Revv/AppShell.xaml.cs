namespace Revv;

public partial class AppShell : Shell
{
    public AppShell(MainPage page)
    {
        InitializeComponent();
        Items.Add(new ShellContent
        {
            Title   = "REVV Phone",
            Content = page,
            Route   = "MainPage"
        });
    }
}
