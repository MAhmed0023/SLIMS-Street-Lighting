namespace Slims.Domain.Entities;

public class Vendor
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>An approved purchase order. Created outside SLIMS; entered or interfaced here so that receipt can be validated against it.</summary>
public class PurchaseOrder
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int VendorId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DeliveryDue { get; set; }
    public int WarrantyMonths { get; set; } = 12;
    public string Status { get; set; } = "Open";        // Open | Closed
    public string? Remarks { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Vendor? Vendor { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int LineNo { get; set; }
    public string Category { get; set; } = "";          // LUM, POL, CBL …
    public string SubCategory { get; set; } = "";       // LED, SOD …
    public string Spec { get; set; } = "";              // 090W, 6M …
    public string Description { get; set; } = "";
    public string Uom { get; set; } = "Nos";
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Article? Article { get; set; }
}

/// <summary>Article created item-code wise against one PO line. The PO reference never leaves the item code.</summary>
public class Article
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = "";          // LUM-LED-090W-PO2611-000482
    public string Category { get; set; } = "";
    public string SubCategory { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Description { get; set; } = "";
    public string Uom { get; set; } = "Nos";
    public bool Serialised { get; set; } = true;
    public decimal PackSize { get; set; } = 1;          // bulk articles: quantity carried by one QR label
    public int PurchaseOrderId { get; set; }
    public int PurchaseOrderLineId { get; set; }
    public int VendorId { get; set; }
    public decimal Rate { get; set; }
    public int WarrantyMonths { get; set; }
    public int? ExpectedLifeMonths { get; set; }
    public decimal ReorderLevel { get; set; }
    public string Status { get; set; } = "Active";      // Active | Blocked | Obsolete
    public int ItemsGenerated { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public PurchaseOrder? PurchaseOrder { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public Vendor? Vendor { get; set; }
}

public class Store
{
    public int Id { get; set; }
    public string Code { get; set; } = "";              // CEN01
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Central";       // Central | Zonal | Sub-store
    public string Zone { get; set; } = "";
    public string? Address { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public string? InChargeUserId { get; set; }
    public bool Active { get; set; } = true;
    public List<StoreLocation> Locations { get; set; } = new();
}

/// <summary>Store ▸ Section ▸ Rack ▸ Bin. Stock always sits here; there is no unassigned state.</summary>
public class StoreLocation
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public string Section { get; set; } = "";
    public string Rack { get; set; } = "";
    public string Bin { get; set; } = "";
    public string FullCode { get; set; } = "";          // CEN01-LUM-R04-B12
    public string Kind { get; set; } = "Stock";         // Stock | Quarantine | Scrap
    public bool Active { get; set; } = true;
    public Store? Store { get; set; }
}

/// <summary>The physical point where material is installed. Evidence is validated against its geo-fence.</summary>
public class Site
{
    public int Id { get; set; }
    public string Code { get; set; } = "";              // SITE-ZA-RD14-0087
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Division { get; set; } = "";
    public string? Ward { get; set; }
    public string Type { get; set; } = "Road stretch";  // Road stretch | Junction | Park | Public building | Pole cluster
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int GeofenceM { get; set; } = 50;
    public string? SupervisorUserId { get; set; }
    public string? OfficerUserId { get; set; }
    public string Status { get; set; } = "Active";      // Planned | Active | Completed | On hold
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
