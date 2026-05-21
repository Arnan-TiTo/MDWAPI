using System.Collections.Generic;

namespace MDWAPI.Models
{
    public class PagedResult<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public IEnumerable<T> Items { get; set; } = new List<T>();
    }
}
