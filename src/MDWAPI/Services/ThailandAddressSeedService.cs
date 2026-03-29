using ClosedXML.Excel;
using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class ThailandAddressSeedService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThailandAddressSeedService> _logger;

    public ThailandAddressSeedService(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<ThailandAddressSeedService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        try
        {
            // await _context.Database.EnsureCreatedAsync(); // AppDbContext is handled in Program.cs

            string excelPath = @"d:\@Project\miniApp2GitVAC\vibeandchicweb\vibeandchicweb\ThepExcel-Thailand-Tambon.xlsx";
            
            if (!File.Exists(excelPath))
            {
                _logger.LogWarning($"Excel file not found at {excelPath}");
                return;
            }

            _logger.LogInformation($"Seeding Thailand address data from {excelPath} (Sheet: postcode)...");

            using var workbook = new XLWorkbook(excelPath);
            var worksheet = workbook.Worksheet("postcode");
            if (worksheet == null)
            {
                _logger.LogError("Worksheet 'postcode' not found in the Excel file.");
                return;
            }

            var headerRow = worksheet.Row(1);
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= worksheet.LastColumnUsed().ColumnNumber(); i++)
            {
                var val = headerRow.Cell(i).GetValue<string>().Trim();
                if (!string.IsNullOrEmpty(val)) colMap[val] = i;
            }

            // Required headers check
            string[] required = { "postcode", "tambonid", "tambonthaishort", "districttahshort", "ptovinceThai" };
            foreach (var req in required)
            {
                if (!colMap.ContainsKey(req))
                {
                    _logger.LogError($"Required column '{req}' not found in 'postcode' sheet.");
                    return;
                }
            }

            var rows = worksheet.RangeUsed().RowsUsed().Skip(1); // Skip header
            var addresses = new List<ThailandAddress>();

            foreach (var row in rows)
            {
                addresses.Add(new ThailandAddress
                {
                    tambonID = row.Cell(colMap["tambonid"]).GetValue<string>().Trim(),
                    subDistrict = row.Cell(colMap["tambonthaishort"]).GetValue<string>().Trim(),
                    district = row.Cell(colMap["districttahshort"]).GetValue<string>().Trim(),
                    province = row.Cell(colMap["ptovinceThai"]).GetValue<string>().Trim(),
                    postcode = row.Cell(colMap["postcode"]).GetValue<string>().Trim()
                });
            }

            if (addresses.Any())
            {
                // Clear existing data
                var existing = await _context.ThailandAddresses.ToListAsync();
                if (existing.Any())
                {
                    _context.ThailandAddresses.RemoveRange(existing);
                    await _context.SaveChangesAsync();
                }

                await _context.ThailandAddresses.AddRangeAsync(addresses);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Successfully seeded {addresses.Count} address records into Thai_Address table.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding Thailand address data.");
        }
    }
}
