using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using PywelTrainer;

public static class StartupSmoke
{
    private static void Dump(Window window, string name)
    {
        object value = window.FindName(name);
        Console.WriteLine("PRE " + name + " = " + (value == null ? "<null>" : value.GetType().FullName));
    }

    [STAThread]
    public static int Main()
    {
        Application app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Window window;
        using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream("PywelTrainer.MainWindow.xaml"))
        {
            if (stream == null) throw new InvalidOperationException("Embedded MainWindow.xaml resource is missing.");
            window = (Window)XamlReader.Load(stream);
        }
        if (window == null) throw new InvalidOperationException("MainWindow.xaml did not create a Window.");

        foreach (string name in new[] { "CatalogList", "CatalogDetailName", "CatalogDescription", "CatalogMetadata", "CatalogAddStatus" }) Dump(window, name);

        MainController controller = new MainController(window, true);
        window.Show();
        window.UpdateLayout();
        window.Close();
        app.Shutdown();
        GC.KeepAlive(controller);
        Console.WriteLine("PASS: trainer WPF startup smoke test");
        return 0;
    }
}
