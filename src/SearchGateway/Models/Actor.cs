using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class Actor
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    [JsonIgnore]
    public ICollection<MovieActor> MovieActors { get; set; } = [];
}
