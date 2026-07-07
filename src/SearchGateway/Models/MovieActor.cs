using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class MovieActor
{
    public int MovieId { get; set; }
    
    [JsonIgnore]
    public Movie Movie { get; set; } = null!;

    public int ActorId { get; set; }
    
    public Actor Actor { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public required string CharacterName { get; set; }
}
