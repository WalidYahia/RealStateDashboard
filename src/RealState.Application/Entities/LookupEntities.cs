using RealState.Application.Common;

namespace RealState.Application.Entities;

public class Country : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public ICollection<City> Cities { get; set; } = new List<City>();
}

public class City : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid CountryId { get; set; }
    public Country? Country { get; set; }
}

public class Currency : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Symbol { get; set; }
}

/// <summary>Organizational section / department (used by projects and HR).</summary>
public class Section : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}
