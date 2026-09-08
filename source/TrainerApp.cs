using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PywelTrainer
{
    public sealed class DisplayItem
    {
        public string Name { get; set; }
        public string Detail { get; set; }
        public Dictionary<string, object> Data { get; set; }
    }

    public sealed class BackendClient : IDisposable
    {
        private Process process;
        private StreamWriter inputWriter;
        private int nextId;
        private bool disposed;
        private readonly object writeLock = new object();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending = new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        public event Action<Dictionary<string, object>> Event;
        public event Action<string> Stopped;
        public bool Running { get { try { return process != null && !process.HasExited && !disposed; } catch { return false; } } }
        public void Start(string basePath)
        {
            string nodePath = Path.Combine(basePath, "runtime", "node.exe");
            string backendPath = Path.Combine(basePath, "app", "backend.js");
            if (!File.Exists(nodePath) || !File.Exists(backendPath))
                throw new FileNotFoundException("Die Trainer-Dateien sind unvollständig. Bitte das komplette Programmverzeichnis verwenden (runtime und app). ");
            ProcessStartInfo start = new ProcessStartInfo(nodePath, "\"" + backendPath + "\"");
            start.WorkingDirectory = basePath;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            process = new Process();
            process.StartInfo = start;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (!String.IsNullOrEmpty(args.Data)) ReadLine(args.Data); };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (!String.IsNullOrEmpty(args.Data)) EmitLog(args.Data); };
            process.Exited += delegate { FailPending("Die Verbindung zum Trainer-Hintergrundprozess wurde beendet."); if (!disposed && Stopped != null) Stopped("Der Hintergrundprozess wurde beendet. Bitte starte den Trainer erneut."); };
            process.Start();
            inputWriter = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            inputWriter.AutoFlush = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        private void EmitLog(string message)
        {
            if (Event != null) Event(new Dictionary<string, object> { { "event", "log" }, { "data", new Dictionary<string, object> { { "level", "info" }, { "message", message } } } });
        }
        private void ReadLine(string line)
        {
            try
            {
                JavaScriptSerializer parser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                Dictionary<string, object> message = parser.Deserialize<Dictionary<string, object>>(line);
                object id;
                if (message.TryGetValue("id", out id))
                {
                    TaskCompletionSource<Dictionary<string, object>> waiter;
                    if (pending.TryRemove(Convert.ToInt32(id), out waiter)) waiter.TrySetResult(message);
                }
                else if (Event != null) Event(message);
            }
            catch (Exception ex) { EmitLog("Antwort konnte nicht gelesen werden: " + ex.Message); }
        }
        public async Task<Dictionary<string, object>> Send(string command, Dictionary<string, object> args, int timeout)
        {
            if (!Running) throw new InvalidOperationException("Der Trainer-Hintergrundprozess ist nicht verfügbar.");
            int id = Interlocked.Increment(ref nextId);
            TaskCompletionSource<Dictionary<string, object>> waiter = new TaskCompletionSource<Dictionary<string, object>>();
            pending.TryAdd(id, waiter);
            try
            {
                string json = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "id", id }, { "cmd", command }, { "args", args ?? new Dictionary<string, object>() } });
                lock (writeLock) { inputWriter.WriteLine(json); }
                Task winner = await Task.WhenAny(waiter.Task, Task.Delay(timeout));
                if (winner != waiter.Task) throw new TimeoutException("Das Spiel hat nicht rechtzeitig geantwortet. Prüfe den Spielstatus und versuche es erneut.");
                Dictionary<string, object> response = await waiter.Task;
                if (!MainController.Flag(response, "ok")) throw new InvalidOperationException(MainController.StringValue(response, "error", "Die Aktion konnte nicht ausgeführt werden."));
                return MainController.Map(response, "result");
            }
            finally { TaskCompletionSource<Dictionary<string, object>> removed; pending.TryRemove(id, out removed); }
        }
        private void FailPending(string error)
        {
            foreach (KeyValuePair<int, TaskCompletionSource<Dictionary<string, object>>> entry in pending)
            {
                TaskCompletionSource<Dictionary<string, object>> waiter;
                if (pending.TryRemove(entry.Key, out waiter)) waiter.TrySetException(new InvalidOperationException(error));
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            FailPending("Trainer wird geschlossen.");
            if (process != null)
            {
                try { lock (writeLock) { if (inputWriter != null) inputWriter.Close(); } } catch { }
                try { if (!process.WaitForExit(1000)) process.Kill(); } catch { }
                process.Dispose();
            }
        }
    }

    public sealed class MainController
    {
        private readonly Window window;
        private readonly BackendClient backend = new BackendClient();
        private Dictionary<string, object> state = new Dictionary<string, object>();
        private List<DisplayItem> destinations = new List<DisplayItem>();
        private List<DisplayItem> inventoryItems = new List<DisplayItem>();
        private bool inventoryLoaded;
        private bool inventoryLoading;
        private bool inventoryPending;
        private int inventoryRevision;
        private bool busy;
        private bool syncing;
        private bool allowClose;
        private bool closing;
        private const int CatalogPageSize = 75;
        private int catalogPage = 1;
        private int catalogTotal;
        private int catalogRevision;
        private bool catalogPending;
        private bool catalogLoading;
        private bool catalogLoaded;
        private readonly DispatcherTimer catalogQueryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly bool preview;
        private readonly string basePath;
        public MainController(Window window, bool preview)
        {
            this.window = window; this.preview = preview;
            basePath = AppDomain.CurrentDomain.BaseDirectory;
            HookNavigation();
            B("AttachButton").Click += async delegate { if (Flag(state, "connected")) await Run("detach", null, "Verbindung wurde getrennt."); else await Run("attach", null, "Verbindung zum Spiel hergestellt."); };
            B("RefreshButton").Click += async delegate { await Run("refresh", null, "Status wurde aktualisiert."); };
            B("InspectButton").Click += async delegate { await Run("inspect", null, "Installation wurde geprüft."); };
            B("BackupButton").Click += async delegate { await Run("backup", null, "Sicherung wurde erstellt."); };
            B("DiagnosticsButton").Click += async delegate { await Run("diagnostics", null, "Diagnose wurde gespeichert."); };
            B("ClearLogButton").Click += delegate { T("LogText").Clear(); };
            HookToggle("HealthToggle", "health"); HookToggle("StaminaToggle", "stamina"); HookToggle("SpiritToggle", "spirit");
            B("CatalogButton").Click += async delegate { await SearchCatalog(); };
            B("CatalogPreviousButton").Click += async delegate { await SearchCatalog(Math.Max(1, catalogPage - 1)); };
            B("CatalogNextButton").Click += async delegate { await SearchCatalog(catalogPage + 1); };
            B("RebuildCatalogButton").Click += async delegate
            {
                InvalidateCatalog();
                Dictionary<string, object> result = await Run("rebuildCatalog", null, "Spieldaten wurden neu eingelesen.");
                if (result != null) await SearchCatalog();
            };
            Find<ComboBox>("CatalogCategory").SelectionChanged += async delegate { await SearchCatalog(); };
            catalogQueryTimer.Tick += async delegate { catalogQueryTimer.Stop(); await SearchCatalog(); };
            T("CatalogQuery").TextChanged += delegate { catalogQueryTimer.Stop(); InvalidateCatalog(); catalogQueryTimer.Start(); };
            T("CatalogQuery").KeyDown += async delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter && B("CatalogButton").IsEnabled) { e.Handled = true; await SearchCatalog(); } };
            B("InventoryButton").Click += async delegate { await LoadInventory(); };
            Find<ComboBox>("InventoryStorage").SelectionChanged += delegate { FilterInventory(); };
            L("CatalogList").SelectionChanged += delegate { ShowCatalogDetails(); UpdateControls(); };
            L("InventoryList").SelectionChanged += delegate { DisplayItem item = L("InventoryList").SelectedItem as DisplayItem; if (item != null) T("InventoryQuantity").Text = StringValue(item.Data, "quantity", "1"); UpdateControls(); };
            L("DestinationsList").SelectionChanged += delegate { UpdateControls(); };
            T("TravelQuery").TextChanged += delegate { FilterDestinations(); };
            B("DestinationsButton").Click += async delegate
            {
                Dictionary<string, object> result = await Run("destinations", null, "Reiseziele wurden geladen.");
                if (result != null) { destinations = ReadItems(result, "destination"); FilterDestinations(); }
            };
            B("AddButton").Click += async delegate
            {
                DisplayItem item = L("CatalogList").SelectedItem as DisplayItem;
                int quantity;
                if (item == null || !Flag(item.Data, "addable") || !ReadQuantity("AddQuantity", out quantity)) return;
                Dictionary<string, object> result = await Run("addItem", AddArguments(item, quantity), "Gegenstand wurde hinzugefügt.");
                if (result != null && Cap("inventory")) await LoadInventory();
            };
            B("QuantityButton").Click += async delegate
            {
                DisplayItem item = L("InventoryList").SelectedItem as DisplayItem;
                int quantity;
                if (item == null || !ReadQuantity("InventoryQuantity", out quantity)) return;
                Dictionary<string, object> result = await Run("setQuantity", new Dictionary<string, object> { { "slot", Raw(item.Data, "slot") }, { "itemId", Raw(item.Data, "itemId") }, { "quantity", quantity } }, "Gegenstandsmenge wurde geändert.");
                if (result != null) await LoadInventory();
            };
            B("TravelButton").Click += async delegate
            {
                DisplayItem item = L("DestinationsList").SelectedItem as DisplayItem;
                if (item != null) await Run("travel", new Dictionary<string, object> { { "sceneId", Raw(item.Data, "sceneId") }, { "nodeIndex", Raw(item.Data, "nodeIndex") } }, "Reise nach " + item.Name + " wurde ausgelöst.");
            };
            backend.Event += delegate(Dictionary<string, object> message) { Dispatch(delegate { ReceiveEvent(message); }); };
            backend.Stopped += delegate(string message) { Dispatch(delegate { MergeState(new Dictionary<string, object> { { "connected", false }, { "playerReady", false }, { "message", message } }); UpdateState(); Log("Fehler", message); }); };
            window.Loaded += async delegate { if (!preview) await StartBackend(); };
            window.Closing += async delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (allowClose || preview) return;
                e.Cancel = true;
                if (closing) return;
                closing = true; window.IsEnabled = false;
                catalogQueryTimer.Stop();
                try { if (backend.Running) await backend.Send("detach", null, 1800); } catch { }
                backend.Dispose(); allowClose = true; window.Close();
            };
            UpdateState();
        }
        private TControl Find<TControl>(string name) where TControl : class { return window.FindName(name) as TControl; }
        private Button B(string name) { return Find<Button>(name); }
        private TextBox T(string name) { return Find<TextBox>(name); }
        private ListBox L(string name) { return Find<ListBox>(name); }
        private void Label(string name, string text) { Find<TextBlock>(name).Text = text; }
        private void Dispatch(Action action) { if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(action); }
        private void HookNavigation()
        {
            string[] names = { "Overview", "Player", "Items", "Travel", "Log" };
            foreach (string name in names)
            {
                string target = name;
                Find<RadioButton>("Nav" + name).Checked += delegate { foreach (string panel in names) Find<StackPanel>(panel + "Panel").Visibility = panel == target ? Visibility.Visible : Visibility.Collapsed; };
            }
        }
        private void HookToggle(string controlName, string valueName)
        {
            CheckBox control = Find<CheckBox>(controlName);
            control.Click += async delegate
            {
                if (syncing) return;
                bool requested = control.IsChecked == true;
                Dictionary<string, object> result = await Run("setToggle", new Dictionary<string, object> { { "name", valueName }, { "value", requested } }, "Auffüllung " + (requested ? "eingeschaltet." : "ausgeschaltet."));
                if (result != null)
                {
                    Dictionary<string, object> toggles = Map(state, "toggles"); toggles[valueName] = requested; state["toggles"] = toggles;
                }
                UpdateState();
            };
        }
        private async Task StartBackend()
        {
            try
            {
                backend.Start(basePath); Label("BackendLabel", "Trainer-Hintergrundprozess bereit"); Log("Info", "Trainer wurde gestartet. Es sind keine Cheats aktiviert."); UpdateControls();
                await Run("status", null, null);
                await Run("inspect", null, null);
                await SearchCatalog();
            }
            catch (Exception ex) { Label("BackendLabel", "Trainer-Dateien prüfen"); state["message"] = ex.Message; UpdateState(); ReportError(ex.Message); }
        }
        private async Task<Dictionary<string, object>> Run(string command, Dictionary<string, object> args, string success)
        {
            if (busy) return null;
            busy = true; UpdateControls(); Label("StatusText", ActionLabel(command));
            try
            {
                Dictionary<string, object> result = await backend.Send(command, args, command == "rebuildCatalog" ? 600000 : command == "attach" || command == "catalog" ? 90000 : 45000);
                if (result.ContainsKey("connected")) MergeState(result);
                if (result.ContainsKey("state")) MergeState(Map(result, "state"));
                foreach (string key in new[] { "catalogAvailable", "catalogCount", "catalogSource", "catalogBuilding", "catalogMessage" }) if (result.ContainsKey(key)) state[key] = result[key];
                if (result.ContainsKey("buildId")) state["buildId"] = Raw(result, "buildId");
                if (result.ContainsKey("fileVersion")) state["fileVersion"] = Raw(result, "fileVersion");
                string message = StringValue(result, "message", success ?? "Bereit.");
                Label("StatusText", message);
                if (!String.IsNullOrWhiteSpace(message) && message != "Bereit.") Log("Info", message);
                UpdateState();
                return result;
            }
            catch (Exception ex) { ReportError(ex.Message); return null; }
            finally
            {
                busy = false; UpdateControls();
                if (catalogPending && !catalogLoading && CatalogAvailable()) Dispatch(async delegate { await LoadCatalogPages(); });
                QueuePendingInventory();
            }
        }
        private string ActionLabel(string command)
        {
            switch (command)
            {
                case "attach": return "Verbindung und verfügbare Funktionen werden geprüft …";
                case "inspect": return "Installation wird geprüft …";
                case "inventory": return "Inventar wird aus dem Spiel gelesen …";
                case "catalog": return "Gegenstandskatalog wird durchsucht …";
                case "rebuildCatalog": return "Gegenstände werden aus den Spieldaten eingelesen …";
                case "destinations": return "Reiseziele werden aus dem Spiel gelesen …";
                case "backup": return "Spielstände werden gesichert …";
                case "detach": return "Verbindung wird getrennt …";
                default: return "Aktion wird ausgeführt …";
            }
        }
        private bool CatalogAvailable() { return Flag(state, "catalogAvailable") || Flag(Map(state, "capabilities"), "catalog"); }
        private void InvalidateCatalog()
        {
            catalogRevision++;
            L("CatalogList").SelectedItem = null;
            L("CatalogList").ItemsSource = null;
            catalogLoaded = false; catalogTotal = 0;
            Label("CatalogCount", "Katalog wird geladen …");
            ShowCatalogDetails(); UpdateControls();
        }
        private Dictionary<string, object> CatalogArguments()
        {
            ComboBoxItem category = Find<ComboBox>("CatalogCategory").SelectedItem as ComboBoxItem;
            return new Dictionary<string, object> { { "query", T("CatalogQuery").Text.Trim() }, { "category", category == null ? "all" : category.Tag.ToString() }, { "page", catalogPage }, { "pageSize", CatalogPageSize } };
        }
        private async Task SearchCatalog(int page = 1)
        {
            catalogQueryTimer.Stop();
            catalogPage = Math.Max(1, page);
            InvalidateCatalog(); catalogPending = true;
            await LoadCatalogPages();
        }
        private async Task LoadCatalogPages()
        {
            if (catalogLoading || busy || !backend.Running || !CatalogAvailable() || Flag(state, "catalogBuilding")) return;
            catalogLoading = true;
            try
            {
                while (catalogPending && backend.Running && CatalogAvailable() && !Flag(state, "catalogBuilding"))
                {
                    catalogPending = false;
                    int revision = catalogRevision;
                    Dictionary<string, object> result = await Run("catalog", CatalogArguments(), null);
                    if (revision != catalogRevision) continue;
                    if (result != null) ApplyCatalog(result);
                    else Label("CatalogCount", "Katalog konnte nicht geladen werden. Bitte erneut suchen.");
                }
            }
            finally { catalogLoading = false; UpdateControls(); }
        }
        private void ApplyCatalog(Dictionary<string, object> result)
        {
            List<DisplayItem> items = ReadItems(result, "catalog");
            L("CatalogList").SelectedItem = null; L("CatalogList").ItemsSource = items;
            catalogTotal = Integer(result, "total", items.Count);
            catalogPage = Math.Max(1, Integer(result, "page", catalogPage));
            int size = Math.Max(1, Integer(result, "pageSize", CatalogPageSize));
            int pages = Math.Max(1, (int)Math.Ceiling(catalogTotal / (double)size));
            catalogLoaded = true;
            Label("CatalogCount", catalogTotal == 0 ? "Keine passenden Gegenstände gefunden." : catalogTotal.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " Treffer · Seite " + catalogPage + " von " + pages);
            if (result.ContainsKey("catalogSource")) state["catalogSource"] = result["catalogSource"];
            else if (result.ContainsKey("source")) state["catalogSource"] = result["source"];
            if (result.ContainsKey("totalAll")) state["catalogCount"] = result["totalAll"];
            IEnumerable categories = Raw(result, "categories") as IEnumerable;
            if (categories != null) foreach (object record in categories)
            {
                Dictionary<string, object> category = record as Dictionary<string, object>;
                if (category == null) continue;
                foreach (ComboBoxItem option in Find<ComboBox>("CatalogCategory").Items)
                    if (Convert.ToString(option.Tag) == StringValue(category, "id", "")) option.Content = StringValue(category, "label", Convert.ToString(option.Content)) + " (" + Integer(category, "count", 0).ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + ")";
            }
            ShowCatalogDetails(); UpdateState();
        }
        private static int Integer(Dictionary<string, object> values, string key, int fallback)
        {
            int value; return Int32.TryParse(StringValue(values, key, ""), out value) ? value : fallback;
        }
        private static Dictionary<string, object> AddArguments(DisplayItem item, int quantity)
        {
            return new Dictionary<string, object> { { "itemKey", Raw(item.Data, "itemKey") ?? Raw(item.Data, "key") }, { "key", Raw(item.Data, "key") ?? Raw(item.Data, "itemKey") }, { "itemId", Raw(item.Data, "itemId") }, { "quantity", quantity } };
        }
        private static string CategoryLabel(string category)
        {
            switch (category)
            {
                case "weapons": return "Waffe"; case "armor": return "Rüstung"; case "materials": return "Material";
                case "consumables": return "Verbrauchsgegenstand"; case "currency": return "Geld / Währung"; case "quest": return "Questgegenstand";
                case "other": return "Sonstiges"; default: return category;
            }
        }
        private void ShowCatalogDetails()
        {
            DisplayItem selected = L("CatalogList").SelectedItem as DisplayItem;
            Label("CatalogDetailName", selected == null ? "Gegenstand auswählen" : selected.Name);
            string description = selected == null ? "Details und Kennungen stammen aus deiner Spielinstallation." : StringValue(selected.Data, "description", "");
            Label("CatalogDescription", String.IsNullOrWhiteSpace(description) ? "Keine Beschreibung in den Spieldaten." : description);
            if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus", LayoutUnavailable() ? "Hinzufügen nicht verfügbar: Die Spielanbindung muss an diese Version angepasst werden." : CanReadInventory() && !Cap("addItem") ? "Inventar lesbar; Hinzufügen ist für diese Spielversion noch nicht verfügbar." : Flag(state, "playerReady") && Cap("addItem") ? "Wähle einen Gegenstand zum Hinzufügen." : "Zum Hinzufügen einen Spielstand laden und den Trainer verbinden."); return; }
            Dictionary<string, object> item = selected.Data;
            List<string> details = new List<string>();
            details.Add("Kataloggruppe: " + CategoryLabel(StringValue(item, "category", "other")));
            string categorySource = StringValue(item, "categorySource", "");
            if (!String.IsNullOrWhiteSpace(categorySource)) details.Add("Zuordnung: " + categorySource);
            string type = StringValue(item, "type", StringValue(item, "itemType", ""));
            if (!String.IsNullOrWhiteSpace(type) && !categorySource.StartsWith("Aus internem", StringComparison.OrdinalIgnoreCase)) details.Add("Typ: " + type);
            if (!String.IsNullOrWhiteSpace(StringValue(item, "rarity", ""))) details.Add("Seltenheit: " + StringValue(item, "rarity", ""));
            if (Raw(item, "maxStack") != null) details.Add("Max. Stapel: " + StringValue(item, "maxStack", ""));
            if (Raw(item, "itemId") != null) details.Add("Laufzeit-ID: " + StringValue(item, "itemId", ""));
            object ids = Raw(item, "gameIds");
            Dictionary<string, object> gameIds = ids as Dictionary<string, object>;
            if (gameIds != null) foreach (KeyValuePair<string, object> id in gameIds) details.Add(id.Key + ": " + Convert.ToString(id.Value, CultureInfo.InvariantCulture));
            else if (ids != null) details.Add("Spiel-IDs: " + (ids is string ? (string)ids : new JavaScriptSerializer().Serialize(ids)));
            else
            {
                string key = StringValue(item, "itemKey", "");
                if (!String.IsNullOrWhiteSpace(key)) details.Add("Spieldaten-ID: " + key);
                if (Raw(item, "key") != null) details.Add("Interner Name: " + StringValue(item, "key", ""));
            }
            if (StringValue(item, "nameSource", "") == "Interner Spielname") details.Add("Name: interner Spielname; keine deutsche Bezeichnung gefunden");
            Label("CatalogMetadata", String.Join(Environment.NewLine, details));
            Label("CatalogAddStatus", LayoutUnavailable() ? "Hinzufügen nicht verfügbar: Die Spielanbindung muss an diese Version angepasst werden." : CanReadInventory() && !Cap("addItem") ? "Inventar lesbar; Hinzufügen ist für diese Spielversion noch nicht verfügbar." : Flag(item, "addable") && Flag(state, "playerReady") && Cap("addItem") ? "Zum Hinzufügen im Spiel bereit." : StringValue(item, "reason", "Zum Hinzufügen muss die Kennung im laufenden Spiel bestätigt werden."));
        }
        private async Task LoadInventory()
        {
            if (!CanReadInventory() || !backend.Running || inventoryLoading) return;
            if (busy) { inventoryPending = true; return; }
            inventoryPending = false;
            inventoryLoading = true;
            int revision = inventoryRevision;
            try
            {
                Dictionary<string, object> result = await Run("inventory", null, null);
                if (result != null && revision == inventoryRevision && CanReadInventory()) ApplyInventory(result);
            }
            finally { inventoryLoading = false; UpdateControls(); QueuePendingInventory(); }
        }
        private bool CanReadInventory() { return Flag(state, "playerReady") && Cap("inventory"); }
        private void QueuePendingInventory()
        {
            if (inventoryPending && !inventoryLoading && !busy && !closing && backend.Running && CanReadInventory())
                Dispatch(async delegate { if (inventoryPending) await LoadInventory(); });
        }
        private void ApplyInventory(Dictionary<string, object> result)
        {
            inventoryItems = ReadItems(result, "inventory");
            inventoryLoaded = true;
            FilterInventory();
        }
        private static string StorageLabel(string storage)
        {
            switch (storage)
            {
                case "Character": return "Figureninventar";
                case "Money": return "Geld & Marken";
                case "Quest": return "Questgegenstände";
                case "CampWareHouse": return "Lager";
                case "InvisibleInventory": return "Verborgener Spielbereich";
                case "": return "Figureninventar";
                default: return storage;
            }
        }
        private void FilterInventory()
        {
            ComboBoxItem option = Find<ComboBox>("InventoryStorage").SelectedItem as ComboBoxItem;
            string storage = option == null ? "Character" : Convert.ToString(option.Tag);
            DisplayItem previous = L("InventoryList").SelectedItem as DisplayItem;
            List<DisplayItem> filtered = inventoryItems.Where(delegate(DisplayItem item) { return storage == "all" || StringValue(item.Data, "storage", "Character") == storage; }).ToList();
            L("InventoryList").SelectedItem = null;
            L("InventoryList").ItemsSource = filtered;
            if (previous != null)
                L("InventoryList").SelectedItem = filtered.FirstOrDefault(delegate(DisplayItem item) { return StringValue(item.Data, "storage", "Character") == StringValue(previous.Data, "storage", "Character") && StringValue(item.Data, "slot", "") == StringValue(previous.Data, "slot", "") && StringValue(item.Data, "itemId", "") == StringValue(previous.Data, "itemId", ""); });
            Label("InventoryCount", inventoryLoaded ? filtered.Count.ToString() + " belegte Plätze · " + (storage == "all" ? "alle Bereiche" : StorageLabel(storage)) : "Noch nicht geladen");
            UpdateControls();
        }
        private bool ReadQuantity(string control, out int quantity)
        {
            if (!Int32.TryParse(T(control).Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out quantity) || quantity < 1 || quantity > 999999)
            { ReportError("Bitte eine ganze Anzahl zwischen 1 und 999999 eingeben. Das Spiel kann je nach Gegenstand kleinere Grenzen vorgeben."); return false; }
            return true;
        }
        private List<DisplayItem> ReadItems(Dictionary<string, object> result, string kind)
        {
            List<DisplayItem> list = new List<DisplayItem>();
            IEnumerable records = Raw(result, "items") as IEnumerable;
            if (records == null) return list;
            foreach (object record in records)
            {
                Dictionary<string, object> item = record as Dictionary<string, object>;
                if (item == null) continue;
                string id = StringValue(item, "itemId", "–");
                string name = StringValue(item, "name", kind == "destination" ? "Reiseziel" : "Gegenstand " + id);
                string detail = kind == "destination" ? "Vom Spiel erkanntes Reiseziel" : kind == "catalog" ? CategoryLabel(StringValue(item, "category", "other")) + " · " + StringValue(item, "key", StringValue(item, "itemKey", "")) : "ID " + id;
                if (kind == "inventory") detail = "Anzahl " + StringValue(item, "quantity", "–") + " · " + StorageLabel(StringValue(item, "storage", "Character")) + (Raw(item, "category") == null ? "" : " · " + CategoryLabel(StringValue(item, "category", "other")));
                list.Add(new DisplayItem { Name = name, Detail = detail, Data = item });
            }
            return list;
        }
        private void FilterDestinations()
        {
            string query = T("TravelQuery").Text.Trim();
            List<DisplayItem> filtered = destinations.Where(delegate(DisplayItem item) { return item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0; }).ToList();
            L("DestinationsList").ItemsSource = filtered;
            Label("DestinationCount", destinations.Count == 0 ? "Noch keine Reiseziele geladen." : filtered.Count.ToString() + " Reiseziele"); UpdateControls();
        }
        private void ReceiveEvent(Dictionary<string, object> message)
        {
            string kind = StringValue(message, "event", ""); Dictionary<string, object> data = Map(message, "data");
            if (kind == "state")
            {
                bool previousAvailable = CatalogAvailable(); bool previousBuilding = Flag(state, "catalogBuilding"); bool previousConnected = Flag(state, "connected");
                bool previousReady = Flag(state, "playerReady"); bool previousAdd = Cap("addItem");
                MergeState(data); UpdateState();
                if (CatalogAvailable() && (!previousAvailable || previousBuilding && !Flag(state, "catalogBuilding") || previousConnected != Flag(state, "connected") || previousReady != Flag(state, "playerReady") || previousAdd != Cap("addItem")))
                    Dispatch(async delegate { await SearchCatalog(); });
            }
            else if (kind == "log")
            {
                string level = StringValue(data, "level", "info"); string content = StringValue(data, "message", "");
                Log(level == "error" ? "Fehler" : level == "warn" ? "Hinweis" : "Info", content);
            }
        }
        private void MergeState(Dictionary<string, object> update)
        {
            bool previousConnected = Flag(state, "connected");
            bool previousInventory = CanReadInventory();
            foreach (KeyValuePair<string, object> pair in update) state[pair.Key] = pair.Value;
            bool readableInventory = CanReadInventory();
            if (previousInventory != readableInventory)
            {
                inventoryRevision++;
                inventoryPending = readableInventory;
                if (!readableInventory) { inventoryItems.Clear(); inventoryLoaded = false; FilterInventory(); }
                QueuePendingInventory();
            }
            if (previousConnected && !Flag(state, "connected"))
            {
                inventoryItems.Clear(); inventoryLoaded = false; inventoryPending = false; FilterInventory(); destinations.Clear(); FilterDestinations();
                Label("InventoryCount", "Noch nicht geladen"); ShowCatalogDetails();
            }
        }
        private bool Cap(string name) { return Flag(state, "connected") && Flag(Map(state, "capabilities"), name); }
        private bool LayoutUnavailable() { return Flag(state, "connected") && Raw(state, "layoutRecognized") is bool && !Flag(state, "layoutRecognized"); }
        private void UpdateState()
        {
            bool connected = Flag(state, "connected"); bool ready = Flag(state, "playerReady");
            bool incompatible = LayoutUnavailable();
            Label("ConnectionBadge", incompatible ? "Spielanbindung nicht erkannt" : connected ? ready ? "Spiel verbunden" : "Spielstand laden" : "Nicht verbunden");
            Find<System.Windows.Shapes.Ellipse>("StateDot").Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(connected && ready ? "#7BCC9D" : connected ? "#F3BE71" : "#8A97AD"));
            Label("OverviewState", incompatible ? "Spielversion noch nicht unterstützt" : connected ? ready ? "Dein Spiel ist verbunden" : "Warte auf deinen Spielstand" : "Warte auf Crimson Desert");
            Label("OverviewMessage", StringValue(state, "message", "Starte das Spiel über Steam und lade deinen Spielstand. Danach kannst du den Trainer verbinden."));
            Label("ProcessLabel", connected ? "Verbunden mit Spielprozess " + StringValue(state, "pid", "–") : "Kein Spielprozess verbunden");
            string buildId = StringValue(state, "buildId", ""); string fileVersion = StringValue(state, "fileVersion", "");
            Label("BuildLabel", String.IsNullOrWhiteSpace(buildId) ? "Noch nicht geprüft" : "Steam-Build " + buildId);
            Label("VersionLabel", String.IsNullOrWhiteSpace(fileVersion) ? "Die Installation wird beim Start geprüft." : "Dateiversion " + fileVersion);
            Dictionary<string, object> caps = Map(state, "capabilities"); int count = caps.Count(delegate(KeyValuePair<string, object> p) { return p.Key != "catalog" && p.Value is bool && (bool)p.Value; });
            Label("CapabilityLabel", !connected ? "Prüfung nach Verbindung" : count == 0 ? "Keine Spielfunktion erkannt" : count == 1 ? "1 Spielfunktion verfügbar" : count.ToString() + " Spielfunktionen verfügbar");
            Label("HealthValue", "Lebenspunkte: " + Percent("health", "maxHealth"));
            Label("StaminaValue", "Ausdauer: " + Percent("stamina", "maxStamina"));
            Label("SpiritValue", "Geistenergie: " + Percent("spirit", "maxSpirit"));
            Label("PlayerAvailability", incompatible ? "Die Spielerstruktur dieser Version wird nicht erkannt. Die Spielanbindung muss angepasst werden." : !connected ? "Verbinde das Spiel, um die verfügbaren Funktionen zu prüfen." : !ready ? "Lade einen Spielstand, um Spielerfunktionen zu verwenden." : !Cap("health") && !Cap("stamina") && !Cap("spirit") ? "Spielerwerte werden gelesen. Das automatische Auffüllen ist für diese Spielversion noch nicht verfügbar." : "Nur vom Trainer erkannte Werte lassen sich verändern.");
            Label("InventoryAvailability", incompatible ? "Inventar nicht verfügbar: Die Spielanbindung erkennt diese Version noch nicht. Einzelheiten unter Protokoll → Diagnose speichern." : !connected ? "Zum Lesen des Inventars einen Spielstand laden und den Trainer verbinden." : !ready ? "Lade einen Spielstand, um dein Inventar zu lesen." : !Cap("inventory") ? "Inventarzugriff noch nicht erkannt. Einzelheiten stehen im Protokoll." : !Cap("setQuantity") ? "Inventar lesbar; Mengenänderungen sind für diese Spielversion noch nicht verfügbar." : "Wähle einen belegten Platz für eine Mengenänderung.");
            if (incompatible) Label("InventoryCount", "Nicht erkannt – kein leeres Inventar");
            int catalogCount = Integer(state, "catalogCount", 0);
            Label("CatalogSource", Flag(state, "catalogBuilding") ? StringValue(state, "catalogMessage", "Spieldaten werden eingelesen …") : CatalogAvailable() ? catalogCount.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " Gegenstände · " + StringValue(state, "catalogSource", "Spieldateien") : StringValue(state, "catalogMessage", "Katalog wird beim Start aus den Spieldaten geladen."));
            if (!CatalogAvailable() && !catalogLoaded) Label("CatalogCount", Flag(state, "catalogBuilding") ? "Spieldaten werden eingelesen …" : "Noch kein Katalog verfügbar. Spieldaten neu einlesen.");
            ShowCatalogDetails();
            syncing = true;
            Dictionary<string, object> toggles = Map(state, "toggles");
            Find<CheckBox>("HealthToggle").IsChecked = connected && Flag(toggles, "health");
            Find<CheckBox>("StaminaToggle").IsChecked = connected && Flag(toggles, "stamina");
            Find<CheckBox>("SpiritToggle").IsChecked = connected && Flag(toggles, "spirit");
            syncing = false;
            UpdateControls();
        }
        private string Percent(string key, string maximumKey)
        {
            object value = Raw(state, key); object maximumValue = Raw(state, maximumKey);
            if (value == null || maximumValue == null || !Flag(state, "connected")) return "–";
            double current, maximum;
            if (!Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out current) ||
                !Double.TryParse(Convert.ToString(maximumValue, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out maximum) ||
                Double.IsNaN(current) || Double.IsInfinity(current) || Double.IsNaN(maximum) || Double.IsInfinity(maximum) || maximum <= 0 || current < 0) return "–";
            double percentage = current / maximum * 100.0;
            if (Double.IsNaN(percentage) || Double.IsInfinity(percentage)) return "–";
            return percentage.ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " %";
        }
        private void UpdateControls()
        {
            bool running = backend.Running; bool available = running && !busy; bool ready = Flag(state, "playerReady");
            B("AttachButton").Content = Flag(state, "connected") ? "Verbindung trennen" : "Mit Spiel verbinden";
            B("AttachButton").IsEnabled = available;
            B("RefreshButton").IsEnabled = available;
            B("InspectButton").IsEnabled = available;
            B("BackupButton").IsEnabled = available;
            B("DiagnosticsButton").IsEnabled = available;
            Find<CheckBox>("HealthToggle").IsEnabled = available && ready && Cap("health");
            Find<CheckBox>("StaminaToggle").IsEnabled = available && ready && Cap("stamina");
            Find<CheckBox>("SpiritToggle").IsEnabled = available && ready && Cap("spirit");
            bool canBrowse = available && CatalogAvailable() && !Flag(state, "catalogBuilding");
            B("CatalogButton").IsEnabled = canBrowse;
            B("CatalogPreviousButton").IsEnabled = canBrowse && catalogLoaded && catalogPage > 1;
            B("CatalogNextButton").IsEnabled = canBrowse && catalogLoaded && catalogPage * CatalogPageSize < catalogTotal;
            B("RebuildCatalogButton").IsEnabled = available && !Flag(state, "catalogBuilding");
            B("InventoryButton").IsEnabled = available && ready && Cap("inventory");
            Find<ComboBox>("InventoryStorage").IsEnabled = inventoryLoaded;
            T("InventoryQuantity").IsEnabled = available && ready && Cap("setQuantity");
            DisplayItem selectedCatalogItem = L("CatalogList").SelectedItem as DisplayItem;
            bool itemAddable = selectedCatalogItem != null && Flag(selectedCatalogItem.Data, "addable");
            B("AddButton").IsEnabled = available && ready && Cap("addItem") && itemAddable;
            B("AddButton").ToolTip = LayoutUnavailable() ? "Die Spielanbindung muss an diese Version angepasst werden." : selectedCatalogItem != null && !itemAddable ? StringValue(selectedCatalogItem.Data, "reason", "Dieser Gegenstand wird noch nicht unterstützt.") : "Ausgewählten Gegenstand hinzufügen";
            ToolTipService.SetShowOnDisabled(B("AddButton"), true);
            DisplayItem selectedInventoryItem = L("InventoryList").SelectedItem as DisplayItem;
            bool itemEditable = selectedInventoryItem != null && !(Raw(selectedInventoryItem.Data, "editable") is bool && !Flag(selectedInventoryItem.Data, "editable"));
            B("QuantityButton").IsEnabled = available && ready && Cap("setQuantity") && itemEditable;
            B("QuantityButton").ToolTip = selectedInventoryItem != null && !itemEditable ? StringValue(selectedInventoryItem.Data, "reason", "Die Menge dieses Gegenstands kann noch nicht bearbeitet werden.") : "Gesamtmenge des ausgewählten Gegenstands setzen";
            ToolTipService.SetShowOnDisabled(B("QuantityButton"), true);
            B("DestinationsButton").IsEnabled = available && ready && Cap("travel");
            B("TravelButton").IsEnabled = available && ready && Cap("travel") && L("DestinationsList").SelectedItem != null;
        }
        private void ReportError(string message) { Label("StatusText", message); Log("Fehler", message); }
        public void LoadPreviewCatalog(string path, string category, int selectedIndex)
        {
            if (!preview) throw new InvalidOperationException("Vorschau nur ohne Spielverbindung verfügbar.");
            Dictionary<string, object> result = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            foreach (ComboBoxItem option in Find<ComboBox>("CatalogCategory").Items) if (Convert.ToString(option.Tag) == category) Find<ComboBox>("CatalogCategory").SelectedItem = option;
            MergeState(new Dictionary<string, object> { { "connected", false }, { "playerReady", false }, { "catalogAvailable", true }, { "catalogCount", Raw(result, "totalAll") ?? Raw(result, "total") }, { "catalogSource", Raw(result, "source") ?? "Spieldateien" } });
            ApplyCatalog(result);
            if (L("CatalogList").Items.Count > 0) L("CatalogList").SelectedIndex = Math.Max(0, Math.Min(selectedIndex, L("CatalogList").Items.Count - 1));
            Label("BackendLabel", "Vorschau der ausgelesenen Spieldaten");
        }
        private void Log(string level, string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return;
            TextBox log = T("LogText");
            if (log.Text.Length > 140000) log.Text = log.Text.Substring(log.Text.Length - 100000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  [" + level + "]  " + message + Environment.NewLine); log.ScrollToEnd();
        }
        public static object Raw(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? value : null; }
        public static string StringValue(Dictionary<string, object> map, string key, string fallback) { object value = Raw(map, key); return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture); }
        public static bool Flag(Dictionary<string, object> map, string key) { object value = Raw(map, key); return value is bool && (bool)value; }
        public static Dictionary<string, object> Map(Dictionary<string, object> map, string key) { return Raw(map, key) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                bool preview = args.Length >= 2 && args[0] == "--render-preview";
                Application app = new Application();
                Window window;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PywelTrainer.MainWindow.xaml")) { window = (Window)XamlReader.Load(stream); }
                MainController controller = new MainController(window, preview);
                if (args.Any(delegate(string argument) { return argument == "--catalog"; })) ((RadioButton)window.FindName("NavItems")).IsChecked = true;
                if (preview)
                {
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -10000; window.Top = -10000; window.ShowInTaskbar = false;
                    window.Show();
                    window.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(delegate { }));
                    window.UpdateLayout();
                    if (args.Length >= 3)
                    {
                        RadioButton tab = window.FindName("Nav" + args[2]) as RadioButton;
                        if (tab != null) tab.IsChecked = true;
                        window.UpdateLayout();
                    }
                    if (args.Length >= 4) { int selectedIndex = 0; if (args.Length >= 6) Int32.TryParse(args[5], out selectedIndex); controller.LoadPreviewCatalog(args[3], args.Length >= 5 ? args[4] : "all", selectedIndex); window.UpdateLayout(); }
                    FrameworkElement root = window.Content as FrameworkElement;
                    RenderTargetBitmap image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    image.Render(root);
                    PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output));
                    using (FileStream file = File.Create(output)) { encoder.Save(file); }
                    window.Close(); app.Shutdown(); GC.KeepAlive(controller); return 0;
                }
                app.Run(window); GC.KeepAlive(controller); return 0;
            }
            catch (Exception ex)
            {
                string errorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trainer-start-error.txt");
                try { File.WriteAllText(errorPath, ex.ToString(), Encoding.UTF8); } catch { }
                if (args.Length == 0) MessageBox.Show("Der Trainer konnte nicht gestartet werden.\n\n" + ex.Message, "Pywel Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
