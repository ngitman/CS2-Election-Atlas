namespace ElectionAtlas.Model
{
    /// <summary>Where a voter group sits in political space, from its traits.</summary>
    public static class LeanModel
    {
        public static Position Lean(in VoterGroup g, LeanWeights w)
        {
            float wealthT = g.WealthBand / (float)(VoterGroup.WealthBands - 1);
            float eduT = g.Education / (float)(VoterGroup.EducationLevels - 1);

            float economic = Lerp(w.WealthEconomicPoorest, w.WealthEconomicRichest, wealthT)
                + Lerp(w.EducationEconomicNone, w.EducationEconomicTop, eduT);
            float social = Lerp(w.EducationSocialNone, w.EducationSocialTop, eduT)
                + (g.Age == AgeBand.Elderly ? w.ElderlySocial : w.AdultSocial);

            switch (g.Job)
            {
                case JobSector.Commercial:
                    economic += w.CommercialEconomic;
                    social += w.CommercialSocial;
                    break;
                case JobSector.Industrial:
                    economic += w.IndustrialEconomic;
                    social += w.IndustrialSocial;
                    break;
                case JobSector.Office:
                    economic += w.OfficeEconomic;
                    social += w.OfficeSocial;
                    break;
                case JobSector.Unemployed:
                    economic += w.UnemployedEconomic;
                    social += w.UnemployedSocial;
                    break;
                case JobSector.Retired:
                    economic += w.RetiredEconomic;
                    social += w.RetiredSocial;
                    break;
                case JobSector.Student:
                    economic += w.StudentEconomic;
                    social += w.StudentSocial;
                    break;
            }

            return new Position(economic, social); // clamps to -1..+1
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
