namespace Shokofin.API.Models
{
    public class Rating
    {
        public decimal Value { get; set; }
        
        public int MaxValue { get; set; }
        
        public string Source { get; set; }
        
        public int Votes { get; set; }
        
        public string Type { get; set; }

        public float ToFloat(uint scale = 1)
        {
            return (float)((Value * scale) / MaxValue);
        }
    }
}