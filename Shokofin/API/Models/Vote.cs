#nullable enable
namespace Shokofin.API.Models
{
    public class Vote
    {
        public decimal Value { get; set; }
        
        public int? MaxValue { get; set; }
        
        public string? Type { get; set; }
    }
}