# API query syntax

List endpoints that take `filter` and `sort` (products, cities, provinces, subcategories, users, employees, employee types) share one small query language. The product list also has typed parameters, and storefront clients should use those first.

## Product list: typed parameters

`GET /api/v1/products`

| Parameter | Type | Meaning |
|---|---|---|
| `q` | string, max 100 | Name **or** description contains the text |
| `categoryId` | int > 0 | Products in any subcategory of this category |
| `subcategoryId` | int > 0 | Products in this subcategory |
| `brandId` | int > 0 | Products of this brand |
| `inStock` | bool | `true`: at least one purchasable offer (active variant, active listing, approved and active seller, active stock > 0). `false`: no such offer |
| `sort` | `newest` \| `rating` \| `name` | `newest` = creation time descending; `rating` = average rating descending, then review count descending; `name` = name ascending. The product id breaks ties in all three |
| `filter` | mini-language | See below |
| `pageNumber`, `pageSize` | int | Paging (`pageSize` at most 100) |
| `includeInactive` | bool | Only applies to callers with catalog-management permission; ignored for everyone else |

**How they combine:** every condition is combined with **AND**. The typed parameters, the `filter` expression and the "active products only" rule all have to hold. Example: `?q=phone&categoryId=3&filter=price ge 1000000` returns active products in category 3 whose name or description contains "phone" and whose price is at least 1,000,000.

**Sort:** `sort` takes one of the preset values above. For backward compatibility it also still takes the mini-language form: comma-separated whitelisted fields, with `-field` or `field desc` for descending (for example `sort=-name`). A preset cannot be mixed with other fields (`sort=newest,name` is rejected).

Price parameters (`minPrice`, `maxPrice`, `sort=price|-price`) arrive with the list-price change (Batch 61).

## Filter mini-language

```text
filter     := or_expr EOF
or_expr    := and_expr ( "or" and_expr )*
and_expr   := unary ( "and" unary )*
unary      := "not" unary | primary
primary    := "(" or_expr ")" | comparison
comparison := FIELD OP VALUE
FIELD      := a whitelisted field; dots navigate (subcategory.subcategoryname)
OP         := eq | ne | gt | ge | lt | le | contains | startswith | endswith
VALUE      := 'text' | "text" | number | true | false | null
```

- Keywords, operators and field names are case-insensitive.
- Precedence: `not` binds tightest, then `and`, then `or`. Use parentheses to change it.
- Text values must be quoted. A text value cannot contain its own quote character; use the other one (`"it's"`). Unquoted words other than `null` are rejected.
- `null` only works with `eq`/`ne` and only on nullable fields.
- `contains`, `startswith`, `endswith` only work on text fields.
- Limits: at most 1000 characters, 20 comparisons, and nesting depth 8 (parentheses and `not`).
- Every token must be consumed. Trailing or missing tokens (`name eq 'a' x`, `name eq`, `name eq 'a' and`, an unclosed parenthesis, a comma) reject the **whole** filter. A filter is never partly applied.

Examples:

```text
name contains 'phone' and price lt 5000000
(brandid eq 3 or brandid eq 7) and not isactive eq false
subcategory.subcategoryname eq 'Laptops'
```

### Whitelisted fields

| Endpoint | Fields |
|---|---|
| products | `name`, `price`, `description`, `subcategory.subcategoryname`, `isactive`, `expirationdate`, `brandid`, `brand.name`, `brand.slug` |
| cities | `cityname`, `provinceid`, `province.provincename` |
| provinces | `provincename` |
| subcategories | `subcategoryname`, `category.categoryname`, `isactive` |
| users | `firstname`, `lastname`, `phonenumber`, `email`, `isactive` |

## Errors

| Case | Status | Body |
|---|---|---|
| `filter` syntax error, field not allowed, value of the wrong type (products) | 400 | `ProblemDetails` with `code: "INVALID_QUERY"` and `errors[]` (`code: "filter_parse_error"`, Persian `message`, `target: "filter"`, `position`) |
| Same, on the other list endpoints | 400 | Same body without the `code` field (the shared error registry comes later) |
| Product typed parameter invalid (`categoryId=-1`, `categoryId=abc`, `inStock=maybe`, `q` too long, unknown `sort`) | 422 | `ValidationProblemDetails` with `code: "VALIDATION_ERROR"` |
