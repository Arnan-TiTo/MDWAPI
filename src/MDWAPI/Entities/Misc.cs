using System;

namespace MDWAPI.Entities
{
    // map: imw.dbo.Misc
    public class Misc
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!;
        public string? Value1 { get; set; }   // schedule: "every:10m" หรือ "09:00,12:00,18:30"
        public string? Value2 { get; set; }   // path: "/api/market/normalize/by-list"
        public string? Value3 { get; set; }   // base query string (ไม่รวม timeFrom/timeTo)
        public string? Value4 { get; set; }   // window spec: "-10m" หรือ "-10m;remember"
        public string? Value5 { get; set; }   // state: lastTo (epoch seconds) เป็น string
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }
}
