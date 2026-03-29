using ClosedXML.Excel;
using System;
using System.Linq;

try
{
    string excelPath = @"d:\@Project\miniApp2GitVAC\vibeandchicweb\vibeandchicweb\ThepExcel-Thailand-Tambon.xlsx";
    using var workbook = new XLWorkbook(excelPath);
    Console.WriteLine("Worksheets:");
    foreach (var sheet in workbook.Worksheets)
    {
        Console.WriteLine($"- {sheet.Name}");
    }

    var postcodeSheet = workbook.Worksheet("Postcode");
    if (postcodeSheet != null)
    {
        Console.WriteLine("\nPostcode Sheet Header:");
        var firstRow = postcodeSheet.Row(1);
        for (int i = 1; i <= 10; i++)
        {
            var cell = firstRow.Cell(i);
            if (cell.IsEmpty()) break;
            Console.WriteLine($"Cell {i}: {cell.GetValue<string>()}");
        }

        Console.WriteLine("\nPostcode Sheet Sample Data (Row 2):");
        var secondRow = postcodeSheet.Row(2);
        for (int i = 1; i <= 10; i++)
        {
            var cell = secondRow.Cell(i);
            if (cell.IsEmpty() && i > 4) break;
            Console.WriteLine($"Cell {i}: {cell.GetValue<string>()}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
