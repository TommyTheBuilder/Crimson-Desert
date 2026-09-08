using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using PywelTrainer;

public static class UiChecks
{
    private static object Invoke(MainController controller, string method, params object[] values) { return typeof(MainController).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Invoke(controller, values); }
    private static void State(MainController controller, Dictionary<string, object> state) { Invoke(controller, "MergeState", state); Invoke(controller, "UpdateState"); }
    private static void SetField(MainController controller, string field, object value) { typeof(MainController).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, value); }
    private static object Field(MainController controller, string field) { return typeof(MainController).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller); }
    private static Dictionary<string, object> InventoryRow(string name, string storage, string slot, int quantity)
    {
        return new Dictionary<string, object> { { "name", name }, { "storage", storage }, { "slot", slot }, { "itemId", 88 }, { "quantity", quantity }, { "category", "materials" }, { "editable", false }, { "reason", "Mengenänderungen noch nicht verfügbar" } };
    }
    private static void Pump(Task task)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until)
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(5);
        }
        if (!task.IsCompleted) throw new TimeoutException("UI request did not finish.");
        task.GetAwaiter().GetResult();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Application app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Window window;
        using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream("PywelTrainer.MainWindow.xaml")) { window = (Window)XamlReader.Load(stream); }
        MainController controller = new MainController(window, true);
        State(controller, new Dictionary<string, object> { { "connected", true }, { "playerReady", false }, { "layoutRecognized", false }, { "capabilities", new Dictionary<string, object> { { "catalog", true }, { "inventory", false }, { "addItem", false } } } });
        if (((TextBlock)window.FindName("ConnectionBadge")).Text != "Spielanbindung nicht erkannt" || ((TextBlock)window.FindName("CapabilityLabel")).Text != "Keine Spielfunktion erkannt" || !((TextBlock)window.FindName("InventoryAvailability")).Text.Contains("Version noch nicht")) throw new Exception("Unsupported build incorrectly shown as unloaded save or available live feature");
        if (((TextBlock)window.FindName("CatalogAddStatus")).Text.Contains("Spielstand laden") || ((Button)window.FindName("InventoryButton")).IsEnabled || ((Button)window.FindName("AddButton")).IsEnabled) throw new Exception("Unsupported build action/status incorrect");
        State(controller, new Dictionary<string, object> { { "layoutRecognized", true } });
        if (((TextBlock)window.FindName("ConnectionBadge")).Text != "Spielstand laden") throw new Exception("Recognized unloaded state not distinguished");
        State(controller, new Dictionary<string, object> { { "connected", false } });
        if (((TextBlock)window.FindName("ConnectionBadge")).Text != "Nicht verbunden") throw new Exception("Disconnected state not distinguished");
        Console.WriteLine("PASS: unsupported build, unloaded save and disconnected states distinguished; offline catalog never counted as live function");
        string[] controls = { "HealthToggle", "StaminaToggle", "SpiritToggle", "AddButton", "QuantityButton", "TravelButton", "CatalogButton", "InventoryButton", "DestinationsButton" };
        foreach (string name in controls) if (((Control)window.FindName(name)).IsEnabled) throw new Exception("Disconnected control enabled: " + name);
        Console.WriteLine("PASS: actions disabled until backend is available");
        typeof(MainController).GetMethod("MergeState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { new Dictionary<string, object> { { "connected", true }, { "playerReady", true }, { "capabilities", new Dictionary<string, object> { { "health", true }, { "stamina", true }, { "spirit", true }, { "addItem", true }, { "setQuantity", true }, { "travel", true } } } } });
        typeof(MainController).GetMethod("UpdateState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
        foreach (string name in controls) if (((Control)window.FindName(name)).IsEnabled) throw new Exception("Control enabled without running backend: " + name);
        Console.WriteLine("PASS: state alone cannot enable controls without backend");
        typeof(MainController).GetMethod("MergeState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { new Dictionary<string, object> { { "health", 75000 }, { "maxHealth", 100000 }, { "stamina", 250 }, { "maxStamina", 1000 }, { "spirit", Double.NaN }, { "maxSpirit", 1000 } } });
        typeof(MainController).GetMethod("UpdateState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
        if (((TextBlock)window.FindName("HealthValue")).Text != "Lebenspunkte: 75 %" || ((TextBlock)window.FindName("StaminaValue")).Text != "Ausdauer: 25 %" || ((TextBlock)window.FindName("SpiritValue")).Text != "Geistenergie: –") throw new Exception("Scaled stat percentage display incorrect");
        typeof(MainController).GetMethod("MergeState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { new Dictionary<string, object> { { "maxHealth", 0 }, { "maxStamina", Double.PositiveInfinity }, { "spirit", 1 }, { "maxSpirit", -1 } } });
        typeof(MainController).GetMethod("UpdateState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
        if (((TextBlock)window.FindName("HealthValue")).Text != "Lebenspunkte: –" || ((TextBlock)window.FindName("StaminaValue")).Text != "Ausdauer: –" || ((TextBlock)window.FindName("SpiritValue")).Text != "Geistenergie: –") throw new Exception("Invalid stat maximum was displayed as a percentage");
        Console.WriteLine("PASS: scaled stats display percentages; nonfinite values and invalid maxima display unknown");
        ListBox catalog = (ListBox)window.FindName("CatalogList");
        catalog.ItemsSource = new[] { new DisplayItem { Name = "Unsupported fixture", Detail = "fixture", Data = new Dictionary<string, object> { { "itemId", 10 }, { "addable", false }, { "reason", "Nicht unterstützt" } } } };
        catalog.SelectedIndex = 0;
        Button add = (Button)window.FindName("AddButton");
        if (add.IsEnabled || Convert.ToString(add.ToolTip) != "Nicht unterstützt" || !ToolTipService.GetShowOnDisabled(add)) throw new Exception("Unsupported item gate missing");
        Console.WriteLine("PASS: unsupported catalog row disabled with visible reason tooltip");
        ListBox inventory = (ListBox)window.FindName("InventoryList");
        inventory.ItemsSource = new[] { new DisplayItem { Name = "Protected fixture", Detail = "fixture", Data = new Dictionary<string, object> { { "slot", 3 }, { "itemId", 88 }, { "quantity", 1 }, { "editable", false }, { "reason", "Menge nicht bearbeitbar" } } } };
        inventory.SelectedIndex = 0;
        Button quantityButton = (Button)window.FindName("QuantityButton");
        if (quantityButton.IsEnabled || Convert.ToString(quantityButton.ToolTip) != "Menge nicht bearbeitbar" || !ToolTipService.GetShowOnDisabled(quantityButton)) throw new Exception("Protected quantity gate missing");
        Console.WriteLine("PASS: uneditable inventory row disabled with visible reason tooltip");
        State(controller, new Dictionary<string, object> { { "connected", true }, { "playerReady", true }, { "layoutRecognized", true }, { "capabilities", new Dictionary<string, object> { { "inventory", true }, { "setQuantity", false }, { "addItem", false }, { "health", false }, { "stamina", false }, { "spirit", false } } } });
        Dictionary<string, object> inventorySnapshot = new Dictionary<string, object> { { "items", new object[] { InventoryRow("Libelle", "Character", "0x100", 1), InventoryRow("Brombeere", "Character", "0x200", 5), InventoryRow("Marke", "Money", "0x100", 17), InventoryRow("Brief", "Quest", "0x100", 1), InventoryRow("Kupferbeutel", "CampWareHouse", "0x100", 4), InventoryRow("Interner Gegenstand", "InvisibleInventory", "0x100", 1) } } };
        Invoke(controller, "ApplyInventory", inventorySnapshot);
        ComboBox storageFilter = (ComboBox)window.FindName("InventoryStorage");
        if (inventory.Items.Count != 2 || ((DisplayItem)inventory.Items[0]).Name != "Libelle" || !((TextBlock)window.FindName("InventoryCount")).Text.Contains("2 belegte Plätze · Figureninventar")) throw new Exception("Default inventory filter mixed storage areas");
        if (!((DisplayItem)inventory.Items[1]).Detail.Contains("Anzahl 5") || !((DisplayItem)inventory.Items[1]).Detail.Contains("Figureninventar") || ((DisplayItem)inventory.Items[1]).Detail.Contains("0x200")) throw new Exception("Inventory row lacks quantity/storage or exposes raw address");
        inventory.SelectedIndex = 0;
        storageFilter.SelectedIndex = 1;
        if (inventory.Items.Count != 1 || ((DisplayItem)inventory.Items[0]).Name != "Marke" || inventory.SelectedItem != null || !((TextBlock)window.FindName("InventoryCount")).Text.Contains("Geld & Marken")) throw new Exception("Storage switch retained wrong selection with shared slot/item IDs");
        storageFilter.SelectedIndex = 2;
        if (inventory.Items.Count != 1 || ((DisplayItem)inventory.Items[0]).Name != "Brief") throw new Exception("Quest filter incorrect");
        storageFilter.SelectedIndex = 3;
        if (inventory.Items.Count != 1 || ((DisplayItem)inventory.Items[0]).Name != "Kupferbeutel") throw new Exception("Warehouse filter incorrect");
        storageFilter.SelectedIndex = 4;
        if (inventory.Items.Count != 6 || !((DisplayItem)inventory.Items[5]).Detail.Contains("Verborgener Spielbereich")) throw new Exception("All-storage filter lost hidden inventory");
        inventory.SelectedIndex = 5;
        Invoke(controller, "ApplyInventory", inventorySnapshot);
        if (inventory.SelectedItem == null || ((DisplayItem)inventory.SelectedItem).Name != "Interner Gegenstand") throw new Exception("Refresh failed to preserve exact storage/slot/item selection");
        storageFilter.SelectedIndex = 0;
        inventory.SelectedIndex = 1;
        if (((TextBox)window.FindName("InventoryQuantity")).Text != "5" || quantityButton.IsEnabled || ((TextBox)window.FindName("InventoryQuantity")).IsEnabled || !((TextBlock)window.FindName("InventoryAvailability")).Text.Contains("Inventar lesbar") || !((TextBlock)window.FindName("CatalogAddStatus")).Text.Contains("noch nicht verfügbar") || !((TextBlock)window.FindName("PlayerAvailability")).Text.Contains("Spielerwerte werden gelesen")) throw new Exception("Read-only live state advertised writes or lost selected quantity");
        int inventoryReadyRevision = Convert.ToInt32(Field(controller, "inventoryRevision"));
        State(controller, new Dictionary<string, object> { { "playerReady", true } });
        if (Convert.ToInt32(Field(controller, "inventoryRevision")) != inventoryReadyRevision) throw new Exception("Ordinary status poll queued another inventory generation");
        State(controller, new Dictionary<string, object> { { "playerReady", false } });
        if (inventory.Items.Count != 0 || inventory.SelectedItem != null || (bool)Field(controller, "inventoryPending") || storageFilter.IsEnabled) throw new Exception("Unloaded save retained stale inventory or pending load");
        Console.WriteLine("PASS: German storage filters preserve every area and exact selection; read-only state blocks writes and save unload clears stale inventory");
        string[] tabs = { "Overview", "Player", "Items", "Travel", "Log" };
        foreach (string name in tabs)
        {
            ((RadioButton)window.FindName("Nav" + name)).IsChecked = true;
            if (((StackPanel)window.FindName(name + "Panel")).Visibility != Visibility.Visible) throw new Exception("Navigation failed: " + name);
        }
        Console.WriteLine("PASS: all five navigation views selectable");
        try { new BackendClient().Start(Path.Combine(Path.GetTempPath(), "pywel-does-not-exist-" + Guid.NewGuid().ToString("N"))); throw new Exception("Missing backend unexpectedly started"); }
        catch (FileNotFoundException) { Console.WriteLine("PASS: missing backend detected before process launch"); }
        if (args.Length > 0)
        {
            using (BackendClient transport = new BackendClient())
            {
                transport.Event += delegate(Dictionary<string, object> message) { if (MainController.StringValue(message, "event", "") == "log") Console.WriteLine("Fixture: " + MainController.StringValue(MainController.Map(message, "data"), "message", "")); };
                transport.Start(Path.GetFullPath(args[0]));
                string expected = "Grüße aus Pywel – 金貨";
                Dictionary<string, object> result = Task.Factory.StartNew(async delegate { return await transport.Send("echo", new Dictionary<string, object> { { "query", expected } }, 5000); }).Unwrap().GetAwaiter().GetResult();
                if (MainController.StringValue(result, "query", "") != expected) throw new Exception("UTF-8 command transport corrupted Unicode");
                Console.WriteLine("PASS: UTF-8 stdin/stdout preserves German and Unicode through a child-process roundtrip");
                DisplayItem stableItem = new DisplayItem { Name = "Test", Data = new Dictionary<string, object> { { "itemKey", 123456 }, { "key", "Item_Weapon_Grüße" }, { "itemId", null }, { "addable", false } } };
                Dictionary<string, object> addArgs = (Dictionary<string, object>)Invoke(controller, "AddArguments", stableItem, 2);
                Dictionary<string, object> echoed = Task.Factory.StartNew(async delegate { return await transport.Send("addItem", addArgs, 5000); }).Unwrap().GetAwaiter().GetResult();
                if (Convert.ToInt32(echoed["itemKey"]) != 123456 || Convert.ToString(echoed["key"]) != "Item_Weapon_Grüße" || echoed["itemId"] != null || Convert.ToInt32(echoed["quantity"]) != 2) throw new Exception("Stable item key not preserved by add command");
                Console.WriteLine("PASS: add command preserves numeric itemKey, exact internal key and null runtime ID");
            }
            BackendClient uiBackend = (BackendClient)Field(controller, "backend");
            uiBackend.Start(Path.GetFullPath(args[0]));
            SetField(controller, "busy", true);
            State(controller, new Dictionary<string, object> { { "connected", true }, { "playerReady", true }, { "capabilities", new Dictionary<string, object> { { "inventory", true }, { "setQuantity", false }, { "addItem", false } } } });
            int queuedInventoryRevision = Convert.ToInt32(Field(controller, "inventoryRevision"));
            if (!(bool)Field(controller, "inventoryPending") || (bool)Field(controller, "inventoryLoading")) throw new Exception("Inventory availability did not queue a deferred read while another action was busy");
            State(controller, new Dictionary<string, object> { { "playerReady", true } });
            if (Convert.ToInt32(Field(controller, "inventoryRevision")) != queuedInventoryRevision) throw new Exception("Steady status unexpectedly restarted inventory loading");
            SetField(controller, "busy", false);
            Invoke(controller, "QueuePendingInventory");
            Pump(Task.Delay(150));
            if ((bool)Field(controller, "inventoryPending") || !(bool)Field(controller, "inventoryLoaded")) throw new Exception("Queued inventory did not automatically load when backend became idle");
            State(controller, new Dictionary<string, object> { { "playerReady", true } });
            if ((bool)Field(controller, "inventoryPending")) throw new Exception("Status poll reloaded already loaded inventory");
            Console.WriteLine("PASS: inventory becomes visible automatically once ready; busy work defers the read and steady status does not reload it");
            State(controller, new Dictionary<string, object> { { "connected", false }, { "playerReady", false }, { "catalogAvailable", true }, { "catalogCount", 160 }, { "catalogSource", "Spieldateien · Deutsch" }, { "capabilities", new Dictionary<string, object>() } });
            if (!((Button)window.FindName("CatalogButton")).IsEnabled || !((Button)window.FindName("RebuildCatalogButton")).IsEnabled || add.IsEnabled || quantityButton.IsEnabled) throw new Exception("Offline catalog capability gate failed");
            Console.WriteLine("PASS: disconnected catalog browse and rebuild enabled while game writes remain disabled");
            Dictionary<string, object> firstPage = new Dictionary<string, object> { { "items", new object[] { new Dictionary<string, object> { { "name", "Prüfgegenstand" }, { "description", "Deutsche Beschreibung aus Spieldaten" }, { "itemKey", 123456 }, { "key", "Item_Test" }, { "itemId", null }, { "category", "weapons" }, { "type", "OneHandSword" }, { "categorySource", "Aus internem Spielnamen zugeordnet" }, { "maxStack", "9000000000000000000" }, { "rarity", "Rare" }, { "addable", false }, { "reason", "Laufzeit-ID noch nicht bestätigt" } } } }, { "total", 160 }, { "totalAll", 2000 }, { "page", 1 }, { "pageSize", 75 }, { "source", "Spieldateien · Deutsch" } };
            Invoke(controller, "ApplyCatalog", firstPage); catalog.SelectedIndex = 0;
            if (catalog.Items.Count != 1 || !((Button)window.FindName("CatalogNextButton")).IsEnabled || ((Button)window.FindName("CatalogPreviousButton")).IsEnabled || !((TextBlock)window.FindName("CatalogCount")).Text.Contains("160 Treffer") || !((TextBlock)window.FindName("CatalogSource")).Text.Contains("2.000")) throw new Exception("First page count/source/navigation incorrect");
            if (!((TextBlock)window.FindName("CatalogMetadata")).Text.Contains("123456") || ((TextBlock)window.FindName("CatalogDescription")).Text != "Deutsche Beschreibung aus Spieldaten") throw new Exception("Game item metadata missing");
            if (!((TextBlock)window.FindName("CatalogMetadata")).Text.Contains("Aus internem Spielnamen zugeordnet") || !((TextBlock)window.FindName("CatalogMetadata")).Text.Contains("9000000000000000000")) throw new Exception("Classification provenance or full-width stack value lost");
            firstPage["page"] = 3; Invoke(controller, "ApplyCatalog", firstPage);
            if (((Button)window.FindName("CatalogNextButton")).IsEnabled || !((Button)window.FindName("CatalogPreviousButton")).IsEnabled) throw new Exception("Last page navigation incorrect");
            Console.WriteLine("PASS: actual totals, German metadata and first/last-page navigation rendered");
            catalog.SelectedIndex = 0;
            SetField(controller, "busy", true);
            ((ComboBox)window.FindName("CatalogCategory")).SelectedIndex = 1;
            if (catalog.SelectedItem != null || catalog.Items.Count != 0 || Convert.ToInt32(Field(controller, "catalogPage")) != 1 || !(bool)Field(controller, "catalogPending")) throw new Exception("Category change retained stale selection or failed to queue search");
            Dictionary<string, object> pendingArgs = (Dictionary<string, object>)Invoke(controller, "CatalogArguments");
            if (Convert.ToString(pendingArgs["category"]) != "weapons" || Convert.ToInt32(pendingArgs["pageSize"]) > 100) throw new Exception("Category/pageSize request incorrect");
            SetField(controller, "busy", false);
            Pump((Task)Invoke(controller, "LoadCatalogPages"));
            Console.WriteLine("PASS: category change clears stale selection, resets pagination and queues while busy");
            Task oldSearch = (Task)Invoke(controller, "SearchCatalog", 3);
            ((ComboBox)window.FindName("CatalogCategory")).SelectedIndex = 2;
            Pump(oldSearch);
            if (Convert.ToInt32(Field(controller, "catalogPage")) != 1 || Convert.ToString(((Dictionary<string, object>)Invoke(controller, "CatalogArguments"))["category"]) != "armor") throw new Exception("Stale catalog response overrode new category/page");
            Console.WriteLine("PASS: stale in-flight catalog results cannot replace the latest filter");
            State(controller, new Dictionary<string, object> { { "connected", true }, { "playerReady", false }, { "capabilities", new Dictionary<string, object> { { "addItem", false } } } });
            int beforeReadyRevision = Convert.ToInt32(Field(controller, "catalogRevision"));
            Invoke(controller, "ReceiveEvent", new Dictionary<string, object> { { "event", "state" }, { "data", new Dictionary<string, object> { { "connected", true }, { "playerReady", true }, { "capabilities", new Dictionary<string, object> { { "addItem", true } } } } } });
            Pump(Task.Delay(100));
            if (Convert.ToInt32(Field(controller, "catalogRevision")) <= beforeReadyRevision) throw new Exception("Loading a save did not refresh runtime catalog mappings");
            Console.WriteLine("PASS: player-ready/capability transitions automatically refresh catalog runtime mappings");
            firstPage["page"] = 1; Invoke(controller, "ApplyCatalog", firstPage); catalog.SelectedIndex = 0;
            State(controller, new Dictionary<string, object> { { "connected", true }, { "playerReady", true }, { "capabilities", new Dictionary<string, object> { { "addItem", true } } } });
            if (add.IsEnabled) throw new Exception("Offline record enabled write without verified mapping");
            catalog.ItemsSource = new[] { new DisplayItem { Name = "Missing flag", Data = new Dictionary<string, object> { { "itemId", 1 } } } }; catalog.SelectedIndex = 0;
            if (add.IsEnabled) throw new Exception("Missing addable flag was treated as verified");
            Invoke(controller, "ApplyCatalog", firstPage); catalog.SelectedIndex = 0;
            State(controller, new Dictionary<string, object> { { "connected", false }, { "playerReady", false } });
            if (catalog.Items.Count != 1 || !((Button)window.FindName("CatalogButton")).IsEnabled || add.IsEnabled) throw new Exception("Disconnect cleared offline catalog or enabled a write");
            Console.WriteLine("PASS: unverified rows remain non-addable; disconnect preserves browsing and blocks writes");
            uiBackend.Dispose();
        }
        window.Close(); app.Shutdown();
        return 0;
    }
}
