using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Data.Seeds;

public static class QuoteComparisonSeed
{
    public static async Task SeedAsync(FamOsDbContext db)
    {
        // Check if already seeded
        if (await db.ProgramVerticals.AnyAsync(p => p.Slug == "tig-trucking"))
            return;

        var tenantId = 1; // TIG tenant
        var verticalId = Guid.NewGuid();

        // Program Vertical
        var vertical = new ProgramVertical
        {
            Id = verticalId,
            TenantId = tenantId,
            Name = "TIG Trucking",
            Slug = "tig-trucking",
            IsActive = true,
            FaitPresetChips = """[{"label":"Best carrier for claims?","prompt":"Which carrier is best for a trucking account with prior claims?"},{"label":"Markel vs Philadelphia?","prompt":"Compare Markel vs Philadelphia across all lines."},{"label":"Best for requirements?","prompt":"What package best meets all checked requirements?"}]"""
        };
        db.ProgramVerticals.Add(vertical);

        // Lines of Business
        var lines = new List<LineOfBusiness>
        {
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "gl", Name = "General Liability", Icon = "🛡️", MetaDescription = "Per occurrence · aggregate · trucking ops", DisplayOrder = 1,
                FieldDefinitions = """[{"key":"policy_type","label":"Policy Type","order":1},{"key":"occ","label":"Per Occurrence","order":2},{"key":"agg","label":"General Aggregate","order":3},{"key":"prod_ops","label":"Products / Comp Ops","order":4},{"key":"pers_adv","label":"Personal & Adv. Injury","order":5},{"key":"fire_legal","label":"Fire Legal Liability","order":6},{"key":"med_exp","label":"Medical Expense","order":7},{"key":"ded","label":"Deductible","order":8},{"key":"hnoa","label":"Hired / Non-Owned Auto","order":9},{"key":"ab","label":"Assault & Battery","order":10},{"key":"es","label":"E&S / Admitted","order":11},{"key":"billing","label":"Billing Type","order":12}]""" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "auto", Name = "Commercial Auto", Icon = "🚛", MetaDescription = "CSL · hired & non-owned · MCS-90", DisplayOrder = 2,
                FieldDefinitions = """[{"key":"policy_type","label":"Policy Type","order":1},{"key":"csl","label":"CSL Limit","order":2},{"key":"hnoa","label":"Hired / Non-Owned Auto","order":3},{"key":"mcs90","label":"MCS-90 Endorsement","order":4},{"key":"um_uim","label":"UM / UIM","order":5},{"key":"pip","label":"PIP","order":6},{"key":"ded","label":"Deductible","order":7},{"key":"es","label":"E&S / Admitted","order":8},{"key":"billing","label":"Billing Type","order":9}]""" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "cargo", Name = "Motor Truck Cargo", Icon = "📦", MetaDescription = "All-risk · refrigeration · loading/unloading", DisplayOrder = 3,
                FieldDefinitions = """[{"key":"limit","label":"Cargo Limit","order":1},{"key":"ded","label":"Deductible","order":2},{"key":"refrig","label":"Refrigeration Breakdown","order":3},{"key":"loading","label":"Loading / Unloading","order":4},{"key":"theft","label":"Theft","order":5},{"key":"debris","label":"Debris Removal","order":6},{"key":"es","label":"E&S / Admitted","order":7},{"key":"billing","label":"Billing Type","order":8}]""" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "pd", Name = "Physical Damage", Icon = "🔧", MetaDescription = "Comprehensive · collision · stated value", DisplayOrder = 4,
                FieldDefinitions = """[{"key":"basis","label":"Value Basis","order":1},{"key":"ded","label":"Deductible","order":2},{"key":"rental","label":"Rental Reimbursement","order":3},{"key":"downtime","label":"Downtime Coverage","order":4},{"key":"towing","label":"Towing","order":5},{"key":"es","label":"E&S / Admitted","order":6},{"key":"billing","label":"Billing Type","order":7}]""" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "umb", Name = "Umbrella / Excess", Icon = "☂️", MetaDescription = "Follows form · excess over GL, auto, cargo", DisplayOrder = 5,
                FieldDefinitions = """[{"key":"limit","label":"Limit","order":1},{"key":"sir","label":"Self-Insured Retention","order":2},{"key":"follow_form","label":"Follow Form","order":3},{"key":"underlying","label":"Required Underlying","order":4},{"key":"epli_exc","label":"EPLI Excess","order":5},{"key":"es","label":"E&S / Admitted","order":6},{"key":"billing","label":"Billing Type","order":7}]""" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "wc", Name = "Workers Comp", Icon = "🏥", MetaDescription = "Statutory · employer liability", DisplayOrder = 6,
                FieldDefinitions = """[{"key":"class_codes","label":"Class Codes","order":1},{"key":"el","label":"Employer Liability","order":2},{"key":"uslh","label":"USL&H","order":3},{"key":"vol_comp","label":"Voluntary Comp","order":4},{"key":"es","label":"E&S / Admitted","order":5},{"key":"billing","label":"Billing Type","order":6}]""" }
        };
        db.LinesOfBusiness.AddRange(lines);

        // Create lookup for line IDs
        var lineBySlug = lines.ToDictionary(l => l.Slug, l => l.Id);

        // Requirements
        var requirements = new List<Requirement>
        {
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-gl-occ", Label = "⚠ Pending TIG ops review: GL — Per Occurrence ($1M min)", GroupName = "General Liability", LineOfBusinessId = lineBySlug["gl"], DisplayOrder = 1 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-gl-agg", Label = "⚠ Pending TIG ops review: GL — Aggregate ($2M min)", GroupName = "General Liability", LineOfBusinessId = lineBySlug["gl"], DisplayOrder = 2 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-gl-hnoa", Label = "⚠ Pending TIG ops review: Hired / Non-Owned Auto", GroupName = "General Liability", LineOfBusinessId = lineBySlug["gl"], DisplayOrder = 3 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-gl-ab", Label = "⚠ Pending TIG ops review: No Assault & Battery Excl.", GroupName = "General Liability", LineOfBusinessId = lineBySlug["gl"], DisplayOrder = 4 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-au-csl", Label = "⚠ Pending TIG ops review: CSL Limit ($1M min)", GroupName = "Commercial Auto", LineOfBusinessId = lineBySlug["auto"], DisplayOrder = 5 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-au-mcs", Label = "⚠ Pending TIG ops review: MCS-90 Endorsement", GroupName = "Commercial Auto", LineOfBusinessId = lineBySlug["auto"], DisplayOrder = 6 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-ca-all", Label = "⚠ Pending TIG ops review: Cargo — All Risk ($250K min)", GroupName = "Motor Truck Cargo", LineOfBusinessId = lineBySlug["cargo"], DisplayOrder = 7 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-ca-ref", Label = "⚠ Pending TIG ops review: Refrigeration Breakdown", GroupName = "Motor Truck Cargo", LineOfBusinessId = lineBySlug["cargo"], DisplayOrder = 8 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-pd-sv", Label = "⚠ Pending TIG ops review: Physical Damage — Stated Value", GroupName = "Physical Damage", LineOfBusinessId = lineBySlug["pd"], DisplayOrder = 9 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-umb", Label = "⚠ Pending TIG ops review: Umbrella — $5M minimum", GroupName = "Umbrella", LineOfBusinessId = lineBySlug["umb"], DisplayOrder = 10 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-wc-stat", Label = "⚠ Pending TIG ops review: WC — Statutory", GroupName = "Workers Comp", LineOfBusinessId = lineBySlug["wc"], DisplayOrder = 11 },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, TenantId = tenantId, Slug = "rq-wc-el", Label = "⚠ Pending TIG ops review: Employer Liability ($500K min)", GroupName = "Workers Comp", LineOfBusinessId = lineBySlug["wc"], DisplayOrder = 12 }
        };
        db.Requirements.AddRange(requirements);

        // Benchmark Premiums
        var benchmarks = new List<BenchmarkPremium>
        {
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, LineOfBusinessId = lineBySlug["gl"], TenantId = tenantId, AnnualPremium = 81500, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Source = "manual" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, LineOfBusinessId = lineBySlug["auto"], TenantId = tenantId, AnnualPremium = 57000, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Source = "manual" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, LineOfBusinessId = lineBySlug["cargo"], TenantId = tenantId, AnnualPremium = 30000, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Source = "manual" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, LineOfBusinessId = lineBySlug["pd"], TenantId = tenantId, AnnualPremium = 19500, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Source = "manual" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, LineOfBusinessId = lineBySlug["umb"], TenantId = tenantId, AnnualPremium = 20500, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Source = "manual" },
            new() { Id = Guid.NewGuid(), ProgramVerticalId = verticalId, LineOfBusinessId = lineBySlug["wc"], TenantId = tenantId, AnnualPremium = 35500, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Source = "manual" }
        };
        db.BenchmarkPremiums.AddRange(benchmarks);

        // Carrier Bundle Rules
        var bundleRules = new List<CarrierBundleRule>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, CarrierName = "Markel", PrimaryLineSlug = "gl", RequiredLineSlug = "cargo", IsActive = true, Notes = "Markel bundles cargo with GL" },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, CarrierName = "Philadelphia", PrimaryLineSlug = "auto", RequiredLineSlug = "pd", IsActive = true, Notes = "Philadelphia bundles PD with Auto" },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, CarrierName = "Progressive", PrimaryLineSlug = "auto", RequiredLineSlug = "pd", IsActive = true, Notes = "Progressive bundles PD with Auto" }
        };
        db.CarrierBundleRules.AddRange(bundleRules);

        await db.SaveChangesAsync();
    }
}
