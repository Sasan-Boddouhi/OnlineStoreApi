using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Queries;

public record Paging(int? Page, int? Size, int? Skip, int? Take)
{
    public static Paging FromPage(int page, int size) => new(page, size, null, null);
    public static Paging FromSkipTake(int skip, int take) => new(null, null, skip, take);
}
