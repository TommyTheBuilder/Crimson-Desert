param(
  [string]$Source = (Join-Path $PSScriptRoot 'TrainerApp.cs'),
  [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) 'PywelTrainer.TrainerApp.generated.cs')
)
$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Source), [Text.Encoding]::UTF8)

function Replace-Required([string]$Old, [string]$New, [string]$Name) {
  if (-not $script:text.Contains($Old)) { throw "TrainerApp-Transformation '$Name' konnte nicht angewendet werden. Der Quelltext ist gedriftet." }
  $script:text = $script:text.Replace($Old, $New)
}

Replace-Required `
'        private bool Cap(string name) { return Flag(state, "connected") && Flag(Map(state, "capabilities"), name); }' `
'        private bool Cap(string name) { return (name == "addItem" || Flag(state, "connected")) && Flag(Map(state, "capabilities"), name); }' `
'addItem capability outside live connection'

Replace-Required `
'            B("AddButton").IsEnabled = available && ready && Cap("addItem") && itemAddable;' `
'            B("AddButton").IsEnabled = available && !Flag(state, "connected") && Cap("addItem") && itemAddable && Integer(state, "saveCount", 0) > 0;' `
'addItem button gating'

$oldNoSelection = '            if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus", LayoutUnavailable() ? "Hinzufügen nicht verfügbar: Die Spielanbindung muss an diese Version angepasst werden." : CanReadInventory() && !Cap("addItem") ? "Inventar lesbar; Hinzufügen ist für diese Spielversion noch nicht verfügbar." : Flag(state, "playerReady") && Cap("addItem") ? "Wähle einen Gegenstand zum Hinzufügen." : "Zum Hinzufügen einen Spielstand laden und den Trainer verbinden."); return; }'
$newNoSelection = '            if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus", !Cap("addItem") ? "Hinzufügen nicht verfügbar: Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Zum sicheren Hinzufügen Crimson Desert vollständig schließen." : Integer(state, "saveCount", 0) == 0 ? "Kein Spielstand gefunden. Speichere einmal im Spiel und schließe Crimson Desert." : "Wähle einen Gegenstand. Ziel: " + StringValue(state, "saveTargetDisplay", "neuester Spielstand") + ". Vor jeder Änderung wird automatisch gesichert."); return; }'
Replace-Required $oldNoSelection $newNoSelection 'catalog empty-selection status'

$oldSelected = '            Label("CatalogAddStatus", LayoutUnavailable() ? "Hinzufügen nicht verfügbar: Die Spielanbindung muss an diese Version angepasst werden." : CanReadInventory() && !Cap("addItem") ? "Inventar lesbar; Hinzufügen ist für diese Spielversion noch nicht verfügbar." : Flag(item, "addable") && Flag(state, "playerReady") && Cap("addItem") ? "Zum Hinzufügen im Spiel bereit." : StringValue(item, "reason", "Zum Hinzufügen muss die Kennung im laufenden Spiel bestätigt werden."));'
$newSelected = '            Label("CatalogAddStatus", !Cap("addItem") ? "Hinzufügen nicht verfügbar: Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Zum sicheren Hinzufügen Crimson Desert vollständig schließen." : Integer(state, "saveCount", 0) == 0 ? "Kein Spielstand gefunden. Speichere einmal im Spiel und schließe Crimson Desert." : Flag(item, "addable") ? "Bereit für sicheren Spielstand-Edit. Ziel: " + StringValue(state, "saveTargetDisplay", "neuester Spielstand") + ". Automatische Sicherung ist aktiv." : StringValue(item, "reason", "Dieser Gegenstand kann nicht sicher hinzugefügt werden."));'
Replace-Required $oldSelected $newSelected 'catalog selected-item status'

$oldTooltip = '            B("AddButton").ToolTip = LayoutUnavailable() ? "Die Spielanbindung muss an diese Version angepasst werden." : selectedCatalogItem != null && !itemAddable ? StringValue(selectedCatalogItem.Data, "reason", "Dieser Gegenstand wird noch nicht unterstützt.") : "Ausgewählten Gegenstand hinzufügen";'
$newTooltip = '            B("AddButton").ToolTip = !Cap("addItem") ? "Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Crimson Desert vollständig schließen; Spielstände werden niemals bearbeitet, solange das Spiel läuft." : Integer(state, "saveCount", 0) == 0 ? "Kein save.save gefunden." : selectedCatalogItem != null && !itemAddable ? StringValue(selectedCatalogItem.Data, "reason", "Dieser Gegenstand wird noch nicht unterstützt.") : "Ausgewählten Gegenstand mit automatischer Sicherung in den neuesten Spielstand einfügen";'
Replace-Required $oldTooltip $newTooltip 'addItem tooltip'

$oldAction = '                case "rebuildCatalog": return "Gegenstände werden aus den Spieldaten eingelesen …";'
$newAction = '                case "rebuildCatalog": return "Gegenstände werden aus den Spieldaten eingelesen …";`r`n                case "addItem": return "Spielstand wird gesichert, geprüft und der Gegenstand eingefügt …";'
Replace-Required $oldAction $newAction 'addItem action label'

$out = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out)) | Out-Null
[IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output $out
