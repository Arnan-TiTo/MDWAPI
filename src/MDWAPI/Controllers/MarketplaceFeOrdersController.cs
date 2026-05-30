using MDWAPI.Data;
using MDWAPI.Models;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/fe/orders")]
    public class MarketplaceFeOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MarketplaceFeOrdersController> _log;
        private readonly ShopeeOrderService _shopee;
        private readonly TiktokOrderService _tiktok;
        private readonly IUnifiedOrderWriter _writer;

        public MarketplaceFeOrdersController(
            AppDbContext db,
            ILogger<MarketplaceFeOrdersController> log,
            ShopeeOrderService shopee,
            TiktokOrderService tiktok,
            IUnifiedOrderWriter writer)
        {
            _db = db;
            _log = log;
            _shopee = shopee;
            _tiktok = tiktok;
            _writer = writer;
        }

        // ====== DTOs ======
        public sealed record UnifiedOrderListItemDto(
            long UnifiedOrderId,
            string? ExternalOrderNo,
            string? Channel,
            long? ShopId,
            string? SellerId,
            string? OrderStatus,
            DateTime? CreatedTimeUtc,
            DateTime? UpdatedTimeUtc,
            string? BuyerUserId,
            string? BuyerUsername,
            decimal? SubtotalAmount,
            decimal? DiscountSellerAmount,
            decimal? DiscountPlatformAmount,
            decimal? VoucherAmount,
            decimal? ShippingFeeAmount,
            decimal? TotalAmount,
            decimal? PaidAmount,
            string? ItemsJson,
            string? PaymentsJson,
            string? ShipmentsJson,
            string? ShipToJson,
            // Escrow / income breakdown
            decimal? EscrowAmount,
            decimal? BuyerPaidShippingFee,
            decimal? ActualShippingFee,
            decimal? PlatformShippingRebate,
            decimal? CommissionFee,
            decimal? ServiceFee,
            decimal? PlatformFee,
            decimal? PaymentTransactionFee,
            decimal? AmsCommissionFee,
            string? SellerVoucherCode,
            FlowAccountAmountsDto FlowAccountAmounts,
            ReconciliationAmountsDto ReconciliationAmounts
        );

        public sealed record FlowAccountAmountsDto(
            decimal GrossAmount,
            decimal SellerDiscountAmount,
            decimal NetAfterSellerDiscountAmount,
            decimal PlatformDiscountAmount,
            decimal AmountDue,
            decimal ShippingFeeAmount,
            decimal TaxAmount,
            decimal GrandTotalAmount
        );

        public sealed record ReconciliationAmountsDto(
            decimal EscrowAmount,
            decimal BuyerPaidShippingFee,
            decimal ActualShippingFee,
            decimal PlatformShippingRebate,
            decimal CommissionFee,
            decimal ServiceFee,
            decimal PlatformFee,
            decimal PaymentTransactionFee,
            decimal AmsCommissionFee,
            decimal TotalFeeAmount,
            decimal NetPayoutAmount,
            string? SellerVoucherCode
        );

        public sealed record PagedResult<T>(
            int TotalItems,
            int Page,
            int Size,
            IEnumerable<T> Items
        );

        // ====== OPTIONS (กัน 405 จาก preflight CORS) ======
        [HttpOptions]
        [AllowAnonymous]
        public IActionResult Options() => Ok();

        // ====== LIST (filter + paging) ======
        // GET /api/fe/orders?channel=TikTok&shopId=123&q=ABC&status=Paid&fromUtc=2025-10-01&toUtc=2025-10-22&page=1&pageSize=50&sort=createdDesc
        [HttpGet]
        public async Task<ActionResult<PagedResult<UnifiedOrderListItemDto>>> List(
            [FromQuery] string? channel,
            [FromQuery] long? shopId,
            [FromQuery] string? q,
            [FromQuery] string? status,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string dateField = "updated",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? sort = "updatedDesc",
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var qy = _db.VUnifiedOrders.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(channel))
                qy = qy.Where(o => o.Channel == channel);

            if (shopId is > 0)
                qy = qy.Where(o => o.ShopId == shopId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                qy = qy.Where(o => o.OrderStatus == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                qy = qy.Where(o =>
                    (o.ExternalOrderNo ?? "").Contains(kw) ||
                    (o.SellerId ?? "").Contains(kw) ||
                    o.UnifiedOrderId.ToString().Contains(kw)
                );
            }

            if (fromUtc.HasValue)
            {
                qy = dateField.ToLowerInvariant() switch
                {
                    "updated" => qy.Where(o => o.UpdatedTimeUtc.HasValue && o.UpdatedTimeUtc.Value >= fromUtc.Value),
                    _ => qy.Where(o => o.CreatedTimeUtc >= fromUtc.Value)
                };
            }

            if (toUtc.HasValue)
            {
                qy = dateField.ToLowerInvariant() switch
                {
                    "updated" => qy.Where(o => o.UpdatedTimeUtc.HasValue && o.UpdatedTimeUtc.Value <= toUtc.Value),
                    _ => qy.Where(o => o.CreatedTimeUtc <= toUtc.Value)
                };
            }

            // sort
            qy = (sort ?? "").ToLowerInvariant() switch
            {
                "createdasc" => qy.OrderBy(o => o.CreatedTimeUtc).ThenBy(o => o.UnifiedOrderId),
                "createddesc" => qy.OrderByDescending(o => o.CreatedTimeUtc).ThenByDescending(o => o.UnifiedOrderId),
                "updatedasc" => qy.OrderBy(o => o.UpdatedTimeUtc).ThenBy(o => o.UnifiedOrderId),
                _ => qy.OrderByDescending(o => o.UpdatedTimeUtc).ThenByDescending(o => o.UnifiedOrderId),
            };

            var total = await qy.CountAsync(ct);

            var pageRows = await qy
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var data = pageRows.Select(ToListItemDto).ToList();

            // *** ตรงกับ PagedResult ของ FE ***
            return Ok(new PagedResult<UnifiedOrderListItemDto>(
                TotalItems: total,
                Page: page,
                Size: pageSize,
                Items: data
            ));
        }

        // ====== GET by UnifiedOrderId ======
        // GET /api/fe/orders/20014
        [HttpGet("{id:long}")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> GetById(long id, CancellationToken ct)
        {
            var o = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleOrDefaultAsync(ct);

            if (o is null) return NotFound();
            return Ok(ToListItemDto(o));
        }

        // ====== GET by (Channel + ExternalOrderNo) ======
        // GET /api/fe/orders/by-external/TikTok/251009MP2J3XNQ
        [HttpGet("by-external/{channel}/{externalOrderNo}")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> GetByExternal(
            string channel, string externalOrderNo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(externalOrderNo))
                return BadRequest("channel & externalOrderNo are required.");

            var o = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.Channel == channel && x.ExternalOrderNo == externalOrderNo)
                .SingleOrDefaultAsync(ct);

            if (o is null) return NotFound();
            return Ok(ToListItemDto(o));
        }

        // POST /api/fe/orders/20014/sync-shopee-escrow
        [HttpPost("{id:long}/sync-shopee-escrow")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> SyncShopeeEscrow(long id, CancellationToken ct)
        {
            var order = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleOrDefaultAsync(ct);

            if (order is null) return NotFound();
            if (!string.Equals(order.Channel, "Shopee", StringComparison.OrdinalIgnoreCase))
                return BadRequest("sync-shopee-escrow supports Shopee orders only.");
            if (order.ShopId is null or <= 0)
                return BadRequest("Shopee shopId is missing.");
            if (string.IsNullOrWhiteSpace(order.ExternalOrderNo))
                return BadRequest("Shopee order number is missing.");

            var json = await _shopee.GetEscrowDetailRawAsync(order.ShopId.Value, order.ExternalOrderNo, ct);
            await _writer.UpsertShopeeEscrowAsync(order.ExternalOrderNo, json, ct);

            var updated = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleAsync(ct);

            return Ok(ToListItemDto(updated));
        }

        // POST /api/fe/orders/20014/sync-tiktok-escrow
        [HttpPost("{id:long}/sync-tiktok-escrow")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> SyncTiktokEscrow(long id, CancellationToken ct)
        {
            var order = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleOrDefaultAsync(ct);

            if (order is null) return NotFound();
            if (!string.Equals(order.Channel, "TikTok", StringComparison.OrdinalIgnoreCase))
                return BadRequest("sync-tiktok-escrow supports TikTok orders only.");
            if (order.ShopId is null or <= 0)
                return BadRequest("TikTok shopId is missing.");
            if (string.IsNullOrWhiteSpace(order.ExternalOrderNo))
                return BadRequest("TikTok order number is missing.");

            var json = await _tiktok.GetOrderEscrowRawAsync(order.ShopId.Value, order.ExternalOrderNo, shopCipher: null, ct);
            await _writer.UpsertTiktokEscrowAsync(order.ExternalOrderNo, json, ct);

            var updated = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleAsync(ct);

            return Ok(ToListItemDto(updated));
        }

        private static UnifiedOrderListItemDto ToListItemDto(VUnifiedOrder o)
        {
            var flow = BuildFlowAccountAmounts(o);
            var reconciliation = BuildReconciliationAmounts(o);

            return new UnifiedOrderListItemDto(
                o.UnifiedOrderId,
                o.ExternalOrderNo,
                o.Channel,
                o.ShopId,
                o.SellerId,
                o.OrderStatus,
                o.CreatedTimeUtc,
                o.UpdatedTimeUtc,
                o.BuyerUserId,
                o.BuyerUsername,
                o.SubtotalAmount,
                o.DiscountSellerAmount,
                o.DiscountPlatformAmount,
                o.VoucherAmount,
                o.ShippingFeeAmount,
                o.TotalAmount,
                o.PaidAmount,
                o.ItemsJson,
                o.PaymentsJson,
                o.ShipmentsJson,
                o.ShipToJson,
                o.EscrowAmount,
                o.BuyerPaidShippingFee,
                o.ActualShippingFee,
                o.PlatformShippingRebate,
                o.CommissionFee,
                o.ServiceFee,
                o.PlatformFee,
                o.PaymentTransactionFee,
                o.AmsCommissionFee,
                o.SellerVoucherCode,
                flow,
                reconciliation
            );
        }

        private static FlowAccountAmountsDto BuildFlowAccountAmounts(VUnifiedOrder o)
        {
            var gross = Money(o.SubtotalAmount);
            var sellerDiscount = Money(o.DiscountSellerAmount);
            var platformDiscount = o.DiscountPlatformAmount.HasValue
                ? Money(o.DiscountPlatformAmount)
                : Money(o.VoucherAmount);
            var shippingFee = Money(o.ShippingFeeAmount);
            var amountDue = Money(o.PaidAmount ?? o.TotalAmount);
            var netAfterSellerDiscount = gross - sellerDiscount;
            var tax = 0m;

            return new FlowAccountAmountsDto(
                GrossAmount: gross,
                SellerDiscountAmount: sellerDiscount,
                NetAfterSellerDiscountAmount: netAfterSellerDiscount,
                PlatformDiscountAmount: platformDiscount,
                AmountDue: amountDue,
                ShippingFeeAmount: shippingFee,
                TaxAmount: tax,
                GrandTotalAmount: amountDue
            );
        }

        private static ReconciliationAmountsDto BuildReconciliationAmounts(VUnifiedOrder o)
        {
            var commissionFee = FeeMoney(o.CommissionFee);
            var serviceFee = FeeMoney(o.ServiceFee);
            var platformFee = FeeMoney(o.PlatformFee);
            var paymentTransactionFee = FeeMoney(o.PaymentTransactionFee);
            var amsCommissionFee = FeeMoney(o.AmsCommissionFee);
            var totalFee = commissionFee + serviceFee + platformFee + paymentTransactionFee + amsCommissionFee;

            return new ReconciliationAmountsDto(
                EscrowAmount: Money(o.EscrowAmount),
                BuyerPaidShippingFee: Money(o.BuyerPaidShippingFee),
                ActualShippingFee: Money(o.ActualShippingFee),
                PlatformShippingRebate: Money(o.PlatformShippingRebate),
                CommissionFee: commissionFee,
                ServiceFee: serviceFee,
                PlatformFee: platformFee,
                PaymentTransactionFee: paymentTransactionFee,
                AmsCommissionFee: amsCommissionFee,
                TotalFeeAmount: totalFee,
                NetPayoutAmount: Money(o.EscrowAmount),
                SellerVoucherCode: o.SellerVoucherCode
            );
        }

        private static decimal Money(decimal? value) => decimal.Round(value ?? 0m, 2);

        private static decimal FeeMoney(decimal? value) => decimal.Round(Math.Abs(value ?? 0m), 2);
    }
}
