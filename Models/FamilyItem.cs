using Autodesk.Revit.DB;

namespace ElementCoordinateExtractor.Models
{
    public class FamilyItem
    {
        public ElementId FamilyId { get; set; }
        public string FamilyName { get; set; }
        public string CategoryName { get; set; }
        public int InstanceCount { get; set; }

        public string DisplayName => $"{FamilyName} ({CategoryName}) - [{InstanceCount} instances]";

        public FamilyItem(ElementId id, string familyName, string categoryName, int instanceCount)
        {
            FamilyId = id;
            FamilyName = familyName;
            CategoryName = categoryName;
            InstanceCount = instanceCount;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
