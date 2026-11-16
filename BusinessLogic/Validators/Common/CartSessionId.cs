namespace BusinessLogic.Validators.Common;

/// <summary>Batch 48 (PM-12): شناسهٔ سبد مهمان باید یک GUID باشد، نه رشتهٔ دلخواه کلاینت.</summary>
public static class CartSessionId
{
    public static bool IsValid(string? value) => Guid.TryParse(value, out var g) && g != Guid.Empty;
}
