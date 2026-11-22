using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class DistrictProfileTests
    {
        private static readonly ProfileConfig Cfg = new ProfileConfig();

        private static DistrictProfile Classify(int households, float homeDensity, float hectares, int industrial = 0, int office = 0, int commercial = 0) =>
            DistrictProfiles.Classify(new DistrictLandUse
            {
                AreaM2 = hectares * 10000f,
                Households = households,
                HomeDensitySum = homeDensity * households,
                IndustrialJobs = industrial,
                OfficeJobs = office,
                CommercialJobs = commercial,
            }, Cfg);

        [Fact]
        public void TowersAreUrbanCore() => Assert.Equal(DistrictProfile.UrbanCore, Classify(2000, 4f, 40));

        [Fact]
        public void VeryDenseMixedLotsAreUrbanCore() => Assert.Equal(DistrictProfile.UrbanCore, Classify(8738, 1.45f, 119.1f));

        [Fact]
        public void RowHousesAreUrban() => Assert.Equal(DistrictProfile.Urban, Classify(800, 0.9f, 60));

        [Fact]
        public void DenseDetachedHousesAreSuburban() => Assert.Equal(DistrictProfile.Suburban, Classify(600, 0.2f, 100));

        [Fact]
        public void SparseDetachedHousesAreRural() => Assert.Equal(DistrictProfile.Rural, Classify(100, 0.2f, 200));

        [Fact]
        public void MostlyFactoryJobsIsIndustrial() => Assert.Equal(DistrictProfile.Industrial, Classify(50, 0.2f, 80, industrial: 900, commercial: 100));

        [Fact]
        public void MostlyOfficeAndShopJobsIsBusiness() => Assert.Equal(DistrictProfile.Business, Classify(100, 2f, 30, industrial: 100, office: 500, commercial: 300));

        [Fact]
        public void JobsBelowTheThresholdStayResidential() => Assert.Equal(DistrictProfile.UrbanCore, Classify(1000, 3f, 30, office: 1500));

        [Fact]
        public void AlmostEmptyIsUnknown() => Assert.Equal(DistrictProfile.Unknown, Classify(5, 1f, 10, commercial: 3));

        [Fact]
        public void JobsWithNoHomesIsAWorkplace() => Assert.Equal(DistrictProfile.Industrial, Classify(0, 0f, 50, industrial: 40));

        [Fact]
        public void NoAreaDoesNotDivideByZero() => Assert.Equal(DistrictProfile.Rural, Classify(100, 0.2f, 0));
    }
}
