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
function Replace-UniqueLine([string]$Contains, [string]$NewLine, [string]$Name) {
  $newline = if ($script:text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $lines = $script:text -split "`r?`n", -1
  $matches = @()
  for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i].Contains($Contains)) { $matches += $i }
  }
  if ($matches.Count -ne 1) { throw "TrainerApp-Transformation '$Name' erwartete genau eine Zeile mit '$Contains', gefunden: $($matches.Count)." }
  $lines[$matches[0]] = $NewLine
  $script:text = $lines -join $newline
}

Replace-Required `
'        private bool Cap(string name) { return Flag(state, "connected") && Flag(Map(state, "capabilities"), name); }' `
'        private bool Cap(string name) { return (name == "addItem" || Flag(state, "connected")) && Flag(Map(state, "capabilities"), name); }' `
'addItem capability outside live connection'

Replace-Required `
'            B("AddButton").IsEnabled = available && ready && Cap("addItem") && itemAddable;' `
'            B("AddButton").IsEnabled = available && !Flag(state, "connected") && Cap("addItem") && itemAddable && Integer(state, "saveCount", 0) > 0;' `
'addItem button gating'

Replace-UniqueLine `
'if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus"' `
'            if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus", !Cap("addItem") ? "Hinzufügen nicht verfügbar: Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Zum sicheren Hinzufügen Crimson Desert vollständig schließen." : Integer(state, "saveCount", 0) == 0 ? "Kein Spielstand gefunden. Speichere einmal im Spiel und schließe Crimson Desert." : "Wähle einen Gegenstand. Ziel: " + StringValue(state, "saveTargetDisplay", "neuester Spielstand") + ". Vor jeder Änderung wird automatisch gesichert."); return; }' `
'catalog empty-selection status'

Replace-UniqueLine `
'Label("CatalogAddStatus", LayoutUnavailable()' `
'            Label("CatalogAddStatus", !Cap("addItem") ? "Hinzufügen nicht verfügbar: Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Zum sicheren Hinzufügen Crimson Desert vollständig schließen." : Integer(state, "saveCount", 0) == 0 ? "Kein Spielstand gefunden. Speichere einmal im Spiel und schließe Crimson Desert." : Flag(item, "addable") ? "Bereit für sicheren Spielstand-Edit. Ziel: " + StringValue(state, "saveTargetDisplay", "neuester Spielstand") + ". Automatische Sicherung ist aktiv." : StringValue(item, "reason", "Dieser Gegenstand kann nicht sicher hinzugefügt werden."));' `
'catalog selected-item status'

Replace-UniqueLine `
'B("AddButton").ToolTip = LayoutUnavailable()' `
'            B("AddButton").ToolTip = !Cap("addItem") ? "Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Crimson Desert vollständig schließen; Spielstände werden niemals bearbeitet, solange das Spiel läuft." : Integer(state, "saveCount", 0) == 0 ? "Kein save.save gefunden." : selectedCatalogItem != null && !itemAddable ? StringValue(selectedCatalogItem.Data, "reason", "Dieser Gegenstand wird noch nicht unterstützt.") : "Ausgewählten Gegenstand mit automatischer Sicherung in den neuesten Spielstand einfügen";' `
'addItem tooltip'

$actionLines = '                case "addItem": return "Spielstand wird gesichert, geprüft und der Gegenstand eingefügt …";' + [Environment]::NewLine + '                default: return "Aktion wird ausgeführt …";'
Replace-UniqueLine 'default: return "Aktion wird ausgeführt' $actionLines 'addItem action label'

$out = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out)) | Out-Null
[IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output $out
