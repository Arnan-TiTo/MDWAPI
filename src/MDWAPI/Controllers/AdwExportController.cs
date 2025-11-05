using ClosedXML.Excel;
using Dapper;
using MDWAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Linq;
using System.Text;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/adw/export")]
    public class AdwExportController : ControllerBase
    {
        private readonly AppDbContext _db;
        public AdwExportController(AppDbContext db) => _db = db;

        /// <summary>
        /// คืนข้อมูลทุกฟิลด์จาก adw.vw_OrderExportFormatTH (แบ่งหน้า) สำหรับ FlowAccount (JSON)
        /// </summary>
        [HttpGet("flowaccount/orders")]
        public async Task<IActionResult> GetFlowAccountOrders(
            [FromQuery] string channel,
            [FromQuery] long shopId,
            [FromQuery] DateTime? createdFrom,
            [FromQuery] DateTime? createdTo,
            [FromQuery] DateTime? updatedFrom,
            [FromQuery] DateTime? updatedTo,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 500,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 5000) pageSize = 500;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var where = new StringBuilder(@"
WHERE channel = @channel AND shop_id = @shopId");

            if (createdFrom.HasValue) where.Append(" AND created_at_th >= @createdFrom");
            if (createdTo.HasValue) where.Append(" AND created_at_th <  @createdToPlus");
            if (updatedFrom.HasValue) where.Append(" AND updated_at_th >= @updatedFrom");
            if (updatedTo.HasValue) where.Append(" AND updated_at_th <  @updatedToPlus");

            var sqlCount = $@"
SELECT COUNT(1)
FROM adw.vw_OrderExportFormatTH
{where};";

            var sqlData = $@"
SELECT *
FROM adw.vw_OrderExportFormatTH
{where}
ORDER BY created_at_th, order_no, qty_sold
OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;";

            DateTime? createdToPlus = createdTo?.Date.AddDays(1);
            DateTime? updatedToPlus = updatedTo?.Date.AddDays(1);

            var p = new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus,
                offset = (page - 1) * pageSize,
                take = pageSize
            };

            var total = await conn.ExecuteScalarAsync<int>(sqlCount, p);
            var items = (await conn.QueryAsync(sqlData, p)).ToList();

            return Ok(new { total, page, pageSize, items });
        }

        /// <summary>
        /// ดาวน์โหลด CSV (UTF-8 BOM) สำหรับ FlowAccount
        /// จะ "ไม่" export 3 ฟิลด์ท้าย: channel, shop_id, updated_at_th
        /// </summary>
        [HttpGet("flowaccount/orders.csv")]
        public async Task<IActionResult> DownloadFlowAccountOrdersCsv(
            [FromQuery] string channel,
            [FromQuery] long shopId,
            [FromQuery] DateTime? createdFrom,
            [FromQuery] DateTime? createdTo,
            [FromQuery] DateTime? updatedFrom,
            [FromQuery] DateTime? updatedTo,
            CancellationToken ct = default)
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var where = new StringBuilder(@"
WHERE channel = @channel AND shop_id = @shopId");

            if (createdFrom.HasValue) where.Append(" AND created_at_th >= @createdFrom");
            if (createdTo.HasValue) where.Append(" AND created_at_th <  @createdToPlus");
            if (updatedFrom.HasValue) where.Append(" AND updated_at_th >= @updatedFrom");
            if (updatedTo.HasValue) where.Append(" AND updated_at_th <  @updatedToPlus");

            var sql = $@"
SELECT *
FROM adw.vw_OrderExportFormatTH
{where}
ORDER BY created_at_th, order_no, qty_sold;";

            DateTime? createdToPlus = createdTo?.Date.AddDays(1);
            DateTime? updatedToPlus = updatedTo?.Date.AddDays(1);

            var rows = (await conn.QueryAsync(sql, new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus
            })).ToList();

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "channel", "shop_id", "updated_at_th" };

            var sb = new StringBuilder();
            byte[] utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };

            if (rows.Count > 0)
            {
                var first = (IDictionary<string, object>)rows[0];
                var headers = first.Keys.Where(k => !blocked.Contains(k)).ToList();
                sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

                foreach (var row in rows)
                {
                    var dict = (IDictionary<string, object>)row;
                    var cols = headers.Select(h => dict.TryGetValue(h, out var v) ? v : null);
                    sb.AppendLine(string.Join(",", cols.Select(CsvEscape)));
                }
            }

            var fileName = $"order-export-flowaccount_{channel}_{shopId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var withBom = utf8Bom.Concat(bytes).ToArray();
            return File(withBom, "text/csv; charset=utf-8", fileName);
        }

        /// <summary>
        /// ดาวน์โหลด Excel (.xlsx) สำหรับ FlowAccount
        /// จะ "ไม่" export 3 ฟิลด์ท้าย: channel, shop_id, updated_at_th
        /// </summary>
        [HttpGet("flowaccount/orders.xlsx")]
        public async Task<IActionResult> DownloadFlowAccountOrdersXlsx(
            [FromQuery] string channel,
            [FromQuery] long shopId,
            [FromQuery] DateTime? createdFrom,
            [FromQuery] DateTime? createdTo,
            [FromQuery] DateTime? updatedFrom,
            [FromQuery] DateTime? updatedTo,
            CancellationToken ct = default)
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var where = new StringBuilder(@"
WHERE channel = @channel AND shop_id = @shopId");

            if (createdFrom.HasValue) where.Append(" AND created_at_th >= @createdFrom");
            if (createdTo.HasValue) where.Append(" AND created_at_th <  @createdToPlus");
            if (updatedFrom.HasValue) where.Append(" AND updated_at_th >= @updatedFrom");
            if (updatedTo.HasValue) where.Append(" AND updated_at_th <  @updatedToPlus");

            var sql = $@"
SELECT *
FROM adw.vw_OrderExportFormatTH
{where}
ORDER BY created_at_th, order_no, qty_sold;";

            DateTime? createdToPlus = createdTo?.Date.AddDays(1);
            DateTime? updatedToPlus = updatedTo?.Date.AddDays(1);

            var rows = (await conn.QueryAsync(sql, new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus
            })).ToList();

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "channel", "shop_id", "updated_at_th" };

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Orders");

            if (rows.Count > 0)
            {
                var first = (IDictionary<string, object>)rows[0];
                var headers = first.Keys.Where(k => !blocked.Contains(k)).ToList();

                // Header
                for (int c = 0; c < headers.Count; c++)
                    ws.Cell(1, c + 1).SetValue(headers[c]);

                // Data
                int r = 2;
                foreach (var row in rows)
                {
                    var dict = (IDictionary<string, object>)row;
                    for (int c = 0; c < headers.Count; c++)
                    {
                        var key = headers[c];
                        dict.TryGetValue(key, out var v);
                        WriteCell(ws.Cell(r, c + 1), v);
                    }
                    r++;
                }

                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                ws.Row(1).Style.Font.SetBold();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var fileName = $"order-export-flowaccount_{channel}_{shopId}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // === Helpers ===

        private static string CsvEscape(object? value)
        {
            var s = value switch
            {
                null => "",
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => value?.ToString() ?? ""
            };
            s = s.Replace("\"", "\"\"");
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                s = $"\"{s}\"";
            return s;
        }

        /// <summary>
        /// เขียนค่าใส่เซลล์ให้ตรงชนิด (แก้ปัญหา XLCellValue)
        /// </summary>
        private static void WriteCell(IXLCell cell, object? v)
        {
            switch (v)
            {
                case null:
                    cell.SetValue(string.Empty);
                    break;

                case DateTime dt:
                    cell.SetValue(dt);
                    cell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    break;

                case DateTimeOffset dto:
                    // ใช้ LocalDateTime/UTC ก็เลือกตามต้องการ; ที่นี่ใช้ Local (ไทย)
                    cell.SetValue(dto.LocalDateTime);
                    cell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    break;

                case bool b:
                    cell.SetValue(b);
                    break;

                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    // เขียนเป็นตัวเลขจำนวนเต็ม
                    cell.SetValue(Convert.ToInt64(v));
                    break;

                case float or double or decimal:
                    // เขียนเป็นตัวเลขทศนิยม
                    cell.SetValue(Convert.ToDouble(v));
                    break;

                default:
                    cell.SetValue(v.ToString() ?? string.Empty);
                    break;
            }
        }
    }
}
