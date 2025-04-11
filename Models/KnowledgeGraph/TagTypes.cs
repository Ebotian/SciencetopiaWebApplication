using System.ComponentModel.DataAnnotations;

public class TagTypes
{
    [Key]
    public Guid TagId { get; set; }
    public int TypeId { get; set; }
}