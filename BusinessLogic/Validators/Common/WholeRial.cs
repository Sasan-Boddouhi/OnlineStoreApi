using Application.Common.Helpers;
using FluentValidation;

namespace BusinessLogic.Validators.Common;

/// <summary>
/// Batch 45b (N6): همهٔ مبالغ ریال کامل‌اند. قیمت یا هزینهٔ کسری در ورودی با 422 VALIDATION_ERROR رد می‌شود تا
/// بعداً مشتری در checkout یا پرداخت به 409 <c>AMOUNT_NOT_WHOLE_RIAL</c> نخورد.
/// </summary>
public static class WholeRial
{
    public static IRuleBuilderOptions<T, decimal> MustBeWholeRial<T>(this IRuleBuilder<T, decimal> rule)
        => rule.Must(RialMoney.IsWhole).WithMessage(RialMoney.NotWholeMessage);
}
