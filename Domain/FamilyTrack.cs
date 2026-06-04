namespace Domain
{
    public class FamilyTrack
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int TrackFamilyId { get; set; }
        public string AssetName { get; set; }
        public bool? IsAdjacent { get; set; }
        public int? AdjacentTypeId { get; set; }
        public string Name { get; set; }
    }
}