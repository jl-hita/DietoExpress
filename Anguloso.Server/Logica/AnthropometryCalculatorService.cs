using System;
using Anguloso.Server.Models;
using Anguloso.Server.Model;

namespace Anguloso.Server.Logica;

public class AnthropometryCalculatorService
{
    public AnthropometryAnalysisDto? Calculate(biometrics b, string gender, int? age)
    {
        if (b == null) return null;

        var isMale = IsMale(gender);
        var height = b.height;
        var weight = b.weight;

        var analysis = new AnthropometryAnalysisDto();

        // 1. Calculate Body Fat Percentages
        analysis.BodyFatPercentageJacksonPollock3 = CalculateJacksonPollock3(b, isMale, age);
        analysis.BodyFatPercentageJacksonPollock4 = CalculateJacksonPollock4(b, isMale, age);
        analysis.BodyFatPercentageJacksonPollock7 = CalculateJacksonPollock7(b, isMale, age);
        analysis.BodyFatPercentageFaulkner = CalculateFaulkner(b);
        analysis.BodyFatPercentageDurninWomersley = CalculateDurninWomersley(b, isMale, age);
        analysis.BodyFatPercentageCarter = CalculateCarter(b, isMale);

        // Determine Body Fat Percentage to use for 4-component model
        double? selectedFatPct = b.body_fat;
        if (!selectedFatPct.HasValue)
        {
            selectedFatPct = analysis.BodyFatPercentageJacksonPollock4 ??
                             analysis.BodyFatPercentageFaulkner ??
                             analysis.BodyFatPercentageDurninWomersley ??
                             analysis.BodyFatPercentageJacksonPollock3 ??
                             analysis.BodyFatPercentageJacksonPollock7 ??
                             analysis.BodyFatPercentageCarter;
        }

        // 2. 4-Component Body Composition (Rocha & Würch)
        if (weight.HasValue && selectedFatPct.HasValue)
        {
            analysis.FatMassKg = Math.Round(weight.Value * (selectedFatPct.Value / 100.0), 2);
            analysis.FatMassPercentage = Math.Round(selectedFatPct.Value, 2);

            // Bone Mass (Rocha)
            if (height.HasValue && b.wrist_diameter.HasValue && b.femur_diameter.HasValue)
            {
                double hM = height.Value / 100.0;
                double wristM = b.wrist_diameter.Value / 100.0;
                double femurM = b.femur_diameter.Value / 100.0;

                double boneMass = 3.02 * Math.Pow(hM * hM * wristM * femurM * 400.0, 0.712);
                analysis.BoneMassKg = Math.Round(boneMass, 2);
                analysis.BoneMassPercentage = Math.Round((boneMass / weight.Value) * 100.0, 2);
            }

            // Residual Mass (Würch)
            double residualFactor = isMale ? 0.241 : 0.209;
            double residualMass = weight.Value * residualFactor;
            analysis.ResidualMassKg = Math.Round(residualMass, 2);
            analysis.ResidualMassPercentage = Math.Round(residualFactor * 100.0, 2);

            // Muscle Mass (by subtraction)
            if (analysis.BoneMassKg.HasValue && analysis.ResidualMassKg.HasValue)
            {
                double muscleMass = weight.Value - (analysis.FatMassKg.Value + analysis.BoneMassKg.Value + analysis.ResidualMassKg.Value);
                analysis.MuscleMassKg = Math.Round(muscleMass, 2);
                analysis.MuscleMassPercentage = Math.Round((muscleMass / weight.Value) * 100.0, 2);
            }
        }

        // 3. Heath-Carter Somatotype
        if (height.HasValue && weight.HasValue && b.triceps.HasValue && b.subscapular.HasValue && b.suprailiac.HasValue)
        {
            var somatotype = new HeathCarterSomatotypeDto();

            // Endomorphy
            double sum3 = b.triceps.Value + b.subscapular.Value + b.suprailiac.Value;
            double sum3Adj = sum3 * (170.18 / height.Value);
            double endo = -0.7182 + (0.1451 * sum3Adj) - (0.00068 * sum3Adj * sum3Adj) + (0.0000014 * sum3Adj * sum3Adj * sum3Adj);
            somatotype.Endomorphy = Math.Max(0.1, Math.Round(endo, 2));

            // Mesomorphy
            if (b.humerus_diameter.HasValue && b.femur_diameter.HasValue && b.arm_perimeter.HasValue && b.calf_perimeter.HasValue && b.calf_skinfold.HasValue)
            {
                double correctedArm = b.arm_perimeter.Value - (b.triceps.Value / 10.0);
                double correctedCalf = b.calf_perimeter.Value - (b.calf_skinfold.Value / 10.0);

                double meso = (0.858 * b.humerus_diameter.Value) +
                             (0.601 * b.femur_diameter.Value) +
                             (0.188 * correctedArm) +
                             (0.161 * correctedCalf) -
                             (height.Value * 0.131) + 4.5;
                somatotype.Mesomorphy = Math.Max(0.1, Math.Round(meso, 2));
            }

            // Ectomorphy (Heath-Carter formula based on Height-Weight Ratio)
            double hwr = height.Value / Math.Pow(weight.Value, 1.0 / 3.0);
            double ecto;
            if (hwr >= 40.75)
            {
                ecto = (0.732 * hwr) - 28.58;
            }
            else if (hwr > 38.25)
            {
                ecto = (0.463 * hwr) - 17.63;
            }
            else
            {
                ecto = 0.1; // Below threshold, minimal ectomorphy
            }
            somatotype.Ectomorphy = Math.Max(0.1, Math.Round(ecto, 2));

            // Always assign the somatotype once Endomorphy and Ectomorphy are computed
            analysis.Somatotype = somatotype;

            // Somatochart coordinates (only calculable if all three components are available)
            if (somatotype.Endomorphy > 0 && somatotype.Mesomorphy > 0 && somatotype.Ectomorphy > 0)
            {
                somatotype.X = Math.Round(somatotype.Ectomorphy - somatotype.Endomorphy, 2);
                somatotype.Y = Math.Round((2.0 * somatotype.Mesomorphy) - (somatotype.Endomorphy + somatotype.Ectomorphy), 2);
                somatotype.CoordinatesAvailable = true;
            }
        }

        return analysis;
    }

    private double? CalculateJacksonPollock3(biometrics b, bool isMale, int? age)
    {
        if (!age.HasValue) return null;

        if (isMale)
        {
            if (!b.chest.HasValue || !b.abdomen.HasValue || !b.thigh.HasValue) return null;
            double sum3 = b.chest.Value + b.abdomen.Value + b.thigh.Value;
            double db = 1.109380 - (0.0008267 * sum3) + (0.0000016 * sum3 * sum3) - (0.0002574 * age.Value);
            return Math.Round((495.0 / db) - 450.0, 2);
        }
        else
        {
            if (!b.triceps.HasValue || !b.suprailiac.HasValue || !b.thigh.HasValue) return null;
            double sum3 = b.triceps.Value + b.suprailiac.Value + b.thigh.Value;
            double db = 1.0994921 - (0.0009929 * sum3) + (0.0000023 * sum3 * sum3) - (0.0001392 * age.Value);
            return Math.Round((495.0 / db) - 450.0, 2);
        }
    }

    private double? CalculateJacksonPollock4(biometrics b, bool isMale, int? age)
    {
        if (!age.HasValue || !b.triceps.HasValue || !b.thigh.HasValue || !b.abdomen.HasValue || !b.suprailiac.HasValue) return null;
        double sum4 = b.triceps.Value + b.thigh.Value + b.abdomen.Value + b.suprailiac.Value;

        if (isMale)
        {
            double db = 1.1096 - (0.0008209 * sum4) + (0.0000016 * sum4 * sum4) - (0.0002574 * age.Value);
            return Math.Round((495.0 / db) - 450.0, 2);
        }
        else
        {
            double db = 1.0960927 - (0.0006952 * sum4) + (0.0000011 * sum4 * sum4) - (0.0000714 * age.Value);
            return Math.Round((495.0 / db) - 450.0, 2);
        }
    }

    private double? CalculateJacksonPollock7(biometrics b, bool isMale, int? age)
    {
        if (!age.HasValue || !b.chest.HasValue || !b.axilla.HasValue || !b.triceps.HasValue || !b.subscapular.HasValue || !b.abdomen.HasValue || !b.suprailiac.HasValue || !b.thigh.HasValue) return null;
        double sum7 = b.chest.Value + b.axilla.Value + b.triceps.Value + b.subscapular.Value + b.abdomen.Value + b.suprailiac.Value + b.thigh.Value;

        if (isMale)
        {
            double db = 1.112 - (0.00043499 * sum7) + (0.00000055 * sum7 * sum7) - (0.00028826 * age.Value);
            return Math.Round((495.0 / db) - 450.0, 2);
        }
        else
        {
            double db = 1.097 - (0.00046971 * sum7) + (0.00000056 * sum7 * sum7) - (0.00012828 * age.Value);
            return Math.Round((495.0 / db) - 450.0, 2);
        }
    }

    private double? CalculateFaulkner(biometrics b)
    {
        if (!b.triceps.HasValue || !b.subscapular.HasValue || !b.suprailiac.HasValue || !b.abdomen.HasValue) return null;
        double sum = b.triceps.Value + b.subscapular.Value + b.suprailiac.Value + b.abdomen.Value;
        return Math.Round((0.153 * sum) + 5.783, 2);
    }

    private double? CalculateDurninWomersley(biometrics b, bool isMale, int? age)
    {
        if (!age.HasValue || !b.biceps.HasValue || !b.triceps.HasValue || !b.subscapular.HasValue || !b.suprailiac.HasValue) return null;
        double sum = b.biceps.Value + b.triceps.Value + b.subscapular.Value + b.suprailiac.Value;
        if (sum <= 0) return null;

        double logSum = Math.Log10(sum);
        double c = 0;
        double m = 0;

        if (isMale)
        {
            if (age.Value < 17) { c = 1.1533; m = 0.0643; }
            else if (age.Value <= 19) { c = 1.1620; m = 0.0630; }
            else if (age.Value <= 29) { c = 1.1631; m = 0.0632; }
            else if (age.Value <= 39) { c = 1.1422; m = 0.0544; }
            else if (age.Value <= 49) { c = 1.1620; m = 0.0700; }
            else { c = 1.1715; m = 0.0779; }
        }
        else
        {
            if (age.Value < 17) { c = 1.1369; m = 0.0598; }
            else if (age.Value <= 19) { c = 1.1549; m = 0.0678; }
            else if (age.Value <= 29) { c = 1.1599; m = 0.0717; }
            else if (age.Value <= 39) { c = 1.1423; m = 0.0632; }
            else if (age.Value <= 49) { c = 1.1333; m = 0.0612; }
            else { c = 1.1339; m = 0.0645; }
        }

        double db = c - (m * logSum);
        return Math.Round((495.0 / db) - 450.0, 2);
    }

    private double? CalculateCarter(biometrics b, bool isMale)
    {
        if (!b.triceps.HasValue || !b.subscapular.HasValue || !b.suprailiac.HasValue || !b.abdomen.HasValue || !b.thigh.HasValue || !b.calf_skinfold.HasValue) return null;
        double sum = b.triceps.Value + b.subscapular.Value + b.suprailiac.Value + b.abdomen.Value + b.thigh.Value + b.calf_skinfold.Value;

        if (isMale)
        {
            return Math.Round((0.1051 * sum) + 2.585, 2);
        }
        else
        {
            return Math.Round((0.1548 * sum) + 3.580, 2);
        }
    }

    private bool IsMale(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return true;
        var norm = gender.Trim().ToLowerInvariant();
        return norm == "m" || norm == "masculino" || norm == "h" || norm == "hombre" || norm == "male";
    }
}
