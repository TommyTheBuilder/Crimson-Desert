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
  $newline = if ($script:text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $lines = $script:text -split "`r?`n", -1
  $matches = @()
  for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i].Contains($Contains)) { $matches += $i }
  }
  if ($matches.Count -ne 1) { throw "TrainerApp transform '$Name' expected exactly one line containing '$Contains', found $($matches.Count)." }
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
'            if (selected == null) { Label("CatalogMetadata", ""); Label("CatalogAddStatus", !Cap("addItem") ? "Hinzuf\u00fcgen nicht verf\u00fcgbar: Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Zum sicheren Hinzuf\u00fcgen Crimson Desert vollst\u00e4ndig schlie\u00dfen." : Integer(state, "saveCount", 0) == 0 ? "Kein Spielstand gefunden. Speichere einmal im Spiel und schlie\u00dfe Crimson Desert." : "W\u00e4hle einen Gegenstand. Ziel: " + StringValue(state, "saveTargetDisplay", "neuester Spielstand") + ". Vor jeder \u00c4nderung wird automatisch gesichert."); return; }' `
'catalog empty-selection status'

Replace-UniqueLine `
'Label("CatalogAddStatus", LayoutUnavailable()' `
'            Label("CatalogAddStatus", !Cap("addItem") ? "Hinzuf\u00fcgen nicht verf\u00fcgbar: Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Zum sicheren Hinzuf\u00fcgen Crimson Desert vollst\u00e4ndig schlie\u00dfen." : Integer(state, "saveCount", 0) == 0 ? "Kein Spielstand gefunden. Speichere einmal im Spiel und schlie\u00dfe Crimson Desert." : Flag(item, "addable") ? "Bereit f\u00fcr sicheren Spielstand-Edit. Ziel: " + StringValue(state, "saveTargetDisplay", "neuester Spielstand") + ". Automatische Sicherung ist aktiv." : StringValue(item, "reason", "Dieser Gegenstand kann nicht sicher hinzugef\u00fcgt werden."));' `
'catalog selected-item status'

Replace-UniqueLine `
'B("AddButton").ToolTip = LayoutUnavailable()' `
'            B("AddButton").ToolTip = !Cap("addItem") ? "Der Save-Editor-Helfer fehlt im runtime-Ordner." : Flag(state, "connected") ? "Crimson Desert vollst\u00e4ndig schlie\u00dfen; Spielst\u00e4nde werden niemals bearbeitet, solange das Spiel l\u00e4uft." : Integer(state, "saveCount", 0) == 0 ? "Kein save.save gefunden." : selectedCatalogItem != null && !itemAddable ? StringValue(selectedCatalogItem.Data, "reason", "Dieser Gegenstand wird noch nicht unterst\u00fctzt.") : "Ausgew\u00e4hlten Gegenstand mit automatischer Sicherung in den neuesten Spielstand einf\u00fcgen";' `
'addItem tooltip'

$actionLines = '                case "addItem": return "Spielstand wird gesichert, gepr\u00fcft und der Gegenstand eingef\u00fcgt \u2026";' + [Environment]::NewLine + '                default: return "Aktion wird ausgef\u00fchrt \u2026";'
Replace-UniqueLine 'default: return "Aktion wird' $actionLines 'addItem action label'

$out = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out)) | Out-Null
[IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output $out
