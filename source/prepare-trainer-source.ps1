param(
  [string]$Source = (Join-Path $PSScriptRoot 'TrainerApp.cs'),
  [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) 'PywelTrainer.TrainerApp.generated.cs')
)
$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Source), [Text.Encoding]::UTF8)

function Replace-Required([string]$Old, [string]$New, [string]$Name) {
  if (-not $script:text.Contains($Old)) { throw "TrainerApp transform '$Name' could not be applied because the source drifted." }
  $script:text = $script:text.Replace($Old, $New)
}
function Replace-UniqueLine([string]$Contains, [string]$NewLine, [string]$Name) {
  # PowerShell variable names are case-insensitive. Do NOT call this local
  # variable $newline: that would overwrite the $NewLine parameter and replace
  # the target C# line with a line break, which caused the v0.5 startup NRE.
  $lineEnding = if ($script:text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $lines = $script:text -split "`r?`n", -1
  $matches = @()
  for ($i = 0; $i -lt $lines.Length; $i++) { if ($lines[$i].Contains($Contains)) { $matches += $i } }
  if ($matches.Count -ne 1) { throw "TrainerApp transform '$Name' expected exactly one line containing '$Contains', found $($matches.Count)." }
  $lines[$matches[0]] = $NewLine
  $script:text = $lines -join $lineEnding
}
function Insert-BeforeUniqueLine([string]$Contains, [string]$NewLine, [string]$Name) {
  $lineEnding = if ($script:text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $lines = $script:text -split "`r?`n", -1
  $matches = @()
  for ($i = 0; $i -lt $lines.Length; $i++) { if ($lines[$i].Contains($Contains)) { $matches += $i } }
  if ($matches.Count -ne 1) { throw "TrainerApp transform '$Name' expected exactly one line containing '$Contains', found $($matches.Count)." }
  $i = $matches[0]
  $before = if ($i -gt 0) { $lines[0..($i-1)] } else { @() }
  $after = $lines[$i..($lines.Length-1)]
  $script:text = @($before + $NewLine + $after) -join $lineEnding
}

Replace-Required `
'            string backendPath = Path.Combine(basePath, "app", "backend.js");' `
'            string backendPath = Path.Combine(basePath, "app", "backend-live.js");' `
'live backend supervisor'

Replace-Required `
'        private TControl Find<TControl>(string name) where TControl : class { return window.FindName(name) as TControl; }' `
'        private TControl Find<TControl>(string name) where TControl : class { TControl value = window.FindName(name) as TControl; if (value != null) return value; value = LogicalTreeHelper.FindLogicalNode(window, name) as TControl; if (value != null) return value; throw new InvalidOperationException("UI-Element fehlt oder hat einen falschen Typ: " + name + " (" + typeof(TControl).Name + ")"); }' `
'WPF logical-tree control lookup'

Replace-Required `
'        private bool Cap(string name) { return Flag(state, "connected") && Flag(Map(state, "capabilities"), name); }' `
'        private bool Cap(string name) { return (name == "addItem" || Flag(state, "connected")) && Flag(Map(state, "capabilities"), name); }' `
'addItem capability outside reader connection'

Replace-Required `
'            B("AddButton").IsEnabled = available && ready && Cap("addItem") && itemAddable;' `
'            B("AddButton").IsEnabled = available && Cap("addItem") && itemAddable;' `
'addItem live/offline button gating'

Replace-UniqueLine `
'if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus"' `
'            if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus", Flag(state, "liveSpawnerReady") ? "Live-Spawner bereit. W\u00e4hle einen Gegenstand; er wird sofort ins laufende Inventar eingef\u00fcgt." : Flag(state, "gameRunning") && Flag(state, "liveSpawnerAvailable") ? StringValue(state, "liveSpawnerMessage", "Live-Komponente wird beim ersten Hinzuf\u00fcgen automatisch geladen.") : Flag(state, "gameRunning") ? "Live-Spawner nicht verf\u00fcgbar: " + StringValue(state, "liveSpawnerMessage", "Live-Komponente fehlt.") : Flag(state, "saveEditorAvailable") && Integer(state, "saveCount", 0) > 0 ? "Crimson Desert ist geschlossen. W\u00e4hle einen Gegenstand f\u00fcr den sicheren Spielstand-Edit; vorher wird automatisch gesichert." : "Kein nutzbarer Live-Spawner oder Spielstand-Editor gefunden."); return; }' `
'catalog empty-selection status'

Replace-UniqueLine `
'Label("CatalogAddStatus", LayoutUnavailable()' `
'            Label("CatalogAddStatus", Flag(state, "liveSpawnerReady") && Flag(item, "addable") ? "LIVE: Wird sofort in dein laufendes Inventar gespawnt." : Flag(state, "gameRunning") && Flag(state, "liveSpawnerAvailable") && Flag(item, "addable") ? "Live-Komponente wird bei Bedarf geladen. " + StringValue(state, "liveSpawnerMessage", "") : Flag(state, "gameRunning") ? "Live-Hinzuf\u00fcgen nicht verf\u00fcgbar: " + StringValue(state, "liveSpawnerMessage", "Live-Komponente fehlt.") : Flag(state, "saveEditorAvailable") && Integer(state, "saveCount", 0) > 0 && Flag(item, "addable") ? "OFFLINE: Wird mit automatischer Sicherung in " + StringValue(state, "saveTargetDisplay", "den neuesten Spielstand") + " eingef\u00fcgt." : StringValue(item, "reason", "Dieser Gegenstand kann noch nicht hinzugef\u00fcgt werden."));' `
'catalog selected-item status'

Replace-UniqueLine `
'B("AddButton").ToolTip = LayoutUnavailable()' `
'            B("AddButton").ToolTip = selectedCatalogItem != null && !itemAddable ? StringValue(selectedCatalogItem.Data, "reason", "Dieser Gegenstand wird noch nicht unterst\u00fctzt.") : Flag(state, "gameRunning") ? (Flag(state, "liveSpawnerReady") ? "Sofort ins laufende Inventar spawnen" : "Live-Komponente automatisch laden und Gegenstand spawnen") : "Bei geschlossenem Spiel mit automatischer Sicherung in den neuesten Spielstand einf\u00fcgen";' `
'addItem tooltip'

Insert-BeforeUniqueLine `
'default: return "Aktion wird' `
'                case "addItem": return "Gegenstand wird hinzugef\u00fcgt \u2026";' `
'addItem action label'

$out = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out)) | Out-Null
[IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output $out