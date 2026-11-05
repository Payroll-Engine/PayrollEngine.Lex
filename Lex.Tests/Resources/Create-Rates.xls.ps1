# Creates Rates.xls (Excel 97-2003 format) for the spreadsheet reader tests.
# Requires Microsoft Excel (COM automation). Run from this folder: .\Create-Rates.xls.ps1
$path = Join-Path $PSScriptRoot "Rates.xls"
$excel = New-Object -ComObject Excel.Application
$excel.DisplayAlerts = $false
try {
    $workbook = $excel.Workbooks.Add()
    while ($workbook.Worksheets.Count -gt 1) { $workbook.Worksheets.Item(2).Delete() }

    $rates = $workbook.Worksheets.Item(1)
    $rates.Name = "Rates"
    $rates.Cells.Item(1, 1).Value2 = "Band"
    $rates.Cells.Item(1, 2).Value2 = "Rate"
    $rates.Cells.Item(1, 3).Value2 = "Double"
    $rates.Cells.Item(2, 1).Value2 = "A"
    $rates.Cells.Item(2, 2).Value2 = 8.5
    $rates.Cells.Item(2, 3).Formula = "=B2*2"
    $rates.Cells.Item(3, 1).Value2 = "B"
    $rates.Cells.Item(3, 2).Value2 = 0.125
    $rates.Cells.Item(4, 1).Value2 = "Since"
    $rates.Cells.Item(4, 2).Value2 = 46023        # 2026-01-01
    $rates.Cells.Item(4, 2).NumberFormat = "yyyy-mm-dd"
    $rates.Cells.Item(5, 1).Value2 = "Active"
    $rates.Cells.Item(5, 2).Value2 = $true

    $limits = $workbook.Worksheets.Add([Type]::Missing, $rates)
    $limits.Name = "Limits"
    $limits.Cells.Item(1, 1).Value2 = "Ceiling"
    $limits.Cells.Item(1, 2).Value2 = 66150

    $workbook.SaveAs($path, 56)                   # xlExcel8
    $workbook.Close($false)
}
finally {
    $excel.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
}
