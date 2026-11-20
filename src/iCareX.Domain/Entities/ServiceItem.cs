namespace iCareX.Domain.Entities;

public sealed class ServiceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MaintenanceRecordId { get; set; }
    public string Type { get; set; } = "Maintenance";
    public string Title { get; set; } = "";
    public decimal Cost { get; set; }
    public Guid? PlanId { get; set; }
}
