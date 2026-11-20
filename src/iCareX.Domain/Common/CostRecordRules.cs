using iCareX.Domain.Entities;
using iCareX.Domain.Exceptions;

namespace iCareX.Domain.Common;

public static class CostRecordRules
{
    public static void Fuel(decimal cost, decimal liters)
    {
        if (cost <= 0 || cost > 10_000_000_000m || decimal.Truncate(cost) != cost
            || liters <= 0 || liters > 500 || decimal.Round(liters, 3) != liters)
            throw new RuleViolation("VALIDATION", "Tiền VNĐ phải là số nguyên dương; số lít trong (0, 500], tối đa 3 số lẻ.");
    }

    public static decimal Service(IReadOnlyList<ServiceItem> items)
    {
        if (items.Count is < 1 or > 50) throw new RuleViolation("VALIDATION", "Dịch vụ cần từ 1 đến 50 hạng mục.");
        foreach (var item in items)
            if (item.Type is not ("Repair" or "Maintenance") || string.IsNullOrWhiteSpace(item.Title)
                || item.Title.Trim().Length is < 2 or > 100 || item.Cost < 0 || item.Cost > 10_000_000_000m
                || decimal.Truncate(item.Cost) != item.Cost || item.Type == "Repair" && item.PlanId is not null)
                throw new RuleViolation("VALIDATION", "Loại, nội dung, chi phí hoặc liên kết hạng mục không hợp lệ.");
        var total = items.Sum(x => x.Cost);
        if (total <= 0 || total > 10_000_000_000m) throw new RuleViolation("VALIDATION", "Tổng chi phí phải trong (0, 10 tỷ] VNĐ.");
        return total;
    }
}
