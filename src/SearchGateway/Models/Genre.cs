using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class Genre
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    [JsonIgnore]
    public List<Movie> Movies { get; set; } = new();
}
