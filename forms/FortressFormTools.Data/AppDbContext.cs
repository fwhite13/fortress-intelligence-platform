using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using FortressFormTools.Data.Entities;

namespace FortressFormTools.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<FormLibrary> FormLibraries => Set<FormLibrary>();
    public DbSet<FormField> FormFields => Set<FormField>();
    public DbSet<DictionaryField> DictionaryFields => Set<DictionaryField>();
    public DbSet<FieldCorrection> FieldCorrections => Set<FieldCorrection>();
    public DbSet<QuestionSet> QuestionSets => Set<QuestionSet>();
    public DbSet<QuestionSetForm> QuestionSetForms => Set<QuestionSetForm>();
    public DbSet<QuestionSetField> QuestionSetFields => Set<QuestionSetField>();
    public DbSet<ToneTemplate> ToneTemplates => Set<ToneTemplate>();
    public DbSet<GeneratedSchema> GeneratedSchemas => Set<GeneratedSchema>();
    public DbSet<FormProject> FormProjects => Set<FormProject>();
    public DbSet<FormFieldCode> FormFieldCodes => Set<FormFieldCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── QuestionSetForm: composite PK ──
        modelBuilder.Entity<QuestionSetForm>()
            .HasKey(qsf => new { qsf.QuestionSetId, qsf.FormLibraryId });

        // ── DictionaryField: unique FieldCode ──
        modelBuilder.Entity<DictionaryField>()
            .HasIndex(d => d.FieldCode)
            .IsUnique();

        // ── FormLibrary indexes ──
        modelBuilder.Entity<FormLibrary>()
            .HasIndex(f => f.Status);
        modelBuilder.Entity<FormLibrary>()
            .HasIndex(f => f.CarrierName);

        // ── FormField: index on FormLibraryId ──
        modelBuilder.Entity<FormField>()
            .HasIndex(ff => ff.FormLibraryId);

        // ── Relationships ──
        modelBuilder.Entity<FormField>()
            .HasOne(ff => ff.FormLibrary)
            .WithMany(fl => fl.Fields)
            .HasForeignKey(ff => ff.FormLibraryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FormField>()
            .HasOne(ff => ff.DictionaryField)
            .WithMany(d => d.FormFields)
            .HasForeignKey(ff => ff.DictionaryFieldId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<FieldCorrection>()
            .HasOne(fc => fc.FormField)
            .WithMany(ff => ff.Corrections)
            .HasForeignKey(fc => fc.FormFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionSetForm>()
            .HasOne(qsf => qsf.QuestionSet)
            .WithMany(qs => qs.QuestionSetForms)
            .HasForeignKey(qsf => qsf.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionSetForm>()
            .HasOne(qsf => qsf.FormLibrary)
            .WithMany(fl => fl.QuestionSetForms)
            .HasForeignKey(qsf => qsf.FormLibraryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuestionSetField>()
            .HasOne(qsf => qsf.QuestionSet)
            .WithMany(qs => qs.Fields)
            .HasForeignKey(qsf => qsf.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GeneratedSchema>()
            .HasOne(gs => gs.QuestionSet)
            .WithMany(qs => qs.GeneratedSchemas)
            .HasForeignKey(gs => gs.QuestionSetId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── FormProject ──
        modelBuilder.Entity<FormProject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Vertical).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasMany(p => p.Documents)
                  .WithOne(f => f.Project)
                  .HasForeignKey(f => f.ProjectId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(p => p.QuestionSets)
                  .WithOne(q => q.Project)
                  .HasForeignKey(q => q.ProjectId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FormLibrary>()
            .HasIndex(f => f.ProjectId);

        modelBuilder.Entity<QuestionSet>()
            .HasIndex(q => q.ProjectId);

        // ── FormFieldCode ──
        modelBuilder.Entity<FormFieldCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => new { e.ProjectId, e.FieldCode }).IsUnique();
            entity.HasOne(e => e.Project)
                  .WithMany()
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed dictionary fields ──
        var seedDate = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<DictionaryField>().HasData(
            new DictionaryField { Id = 1,  FieldCode = "business_name",            DisplayName = "Business Name",              Category = "General",        FieldType = "text",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 2,  FieldCode = "dba_name",                 DisplayName = "DBA / Trade Name",           Category = "General",        FieldType = "text",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 3,  FieldCode = "years_in_business",        DisplayName = "Years in Business",          Category = "General",        FieldType = "number",   IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 4,  FieldCode = "effective_date",           DisplayName = "Policy Effective Date",      Category = "Coverage",       FieldType = "date",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 5,  FieldCode = "expiration_date",          DisplayName = "Policy Expiration Date",     Category = "Coverage",       FieldType = "date",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 6,  FieldCode = "annual_revenue",           DisplayName = "Annual Revenue",             Category = "Financial",      FieldType = "number",   IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 7,  FieldCode = "num_employees",            DisplayName = "Number of Employees",        Category = "General",        FieldType = "number",   IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 8,  FieldCode = "primary_contact_name",     DisplayName = "Primary Contact Name",       Category = "General",        FieldType = "text",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 9,  FieldCode = "primary_contact_phone",    DisplayName = "Primary Contact Phone",      Category = "General",        FieldType = "phone",    IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 10, FieldCode = "primary_contact_email",    DisplayName = "Primary Contact Email",      Category = "General",        FieldType = "email",    IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 11, FieldCode = "mailing_address",          DisplayName = "Mailing Address",            Category = "Location",       FieldType = "address",  IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 12, FieldCode = "location_address",         DisplayName = "Location Address",           Category = "Location",       FieldType = "address",  IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 13, FieldCode = "gl_limit",                 DisplayName = "General Liability Limit",    Category = "Liability",      FieldType = "currency", IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 14, FieldCode = "property_value",           DisplayName = "Property Value",             Category = "Property",       FieldType = "currency", IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 15, FieldCode = "deductible",               DisplayName = "Deductible",                 Category = "Coverage",       FieldType = "currency", IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 16, FieldCode = "description_of_operations",DisplayName = "Description of Operations",  Category = "General",        FieldType = "textarea", IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 17, FieldCode = "naics_code",               DisplayName = "NAICS Code",                 Category = "Classification", FieldType = "text",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 18, FieldCode = "sic_code",                 DisplayName = "SIC Code",                   Category = "Classification", FieldType = "text",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate },
            new DictionaryField { Id = 19, FieldCode = "fein",                     DisplayName = "Federal EIN",                Category = "General",        FieldType = "text",     IsStandard = true, CreatedAt = seedDate, UpdatedAt = seedDate }
        );

        // ── Seed tone templates ──
        modelBuilder.Entity<ToneTemplate>().HasData(
            new ToneTemplate
            {
                Id = 1,
                Name = "Professional (Default)",
                Description = "Standard business language",
                PromptFragment = "Use clear, professional business language. Be direct but polite. Use standard insurance terminology.",
                IsSystem = true,
                CreatedAt = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc)
            },
            new ToneTemplate
            {
                Id = 2,
                Name = "Friendly/Accessible",
                Description = "For churches, nonprofits, community organizations",
                PromptFragment = "Use warm, approachable language. Avoid jargon where possible. Frame questions as conversations. Example: instead of 'Describe premises operations' use 'Please tell us about your facility and what activities take place there.'",
                IsSystem = true,
                CreatedAt = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc)
            },
            new ToneTemplate
            {
                Id = 3,
                Name = "Direct/Technical",
                Description = "For contractors, truckers, industry professionals",
                PromptFragment = "Use concise, no-nonsense language. Industry professionals are filling this out — they know the terminology. Keep questions short and factual. Example: 'List all vehicles. Include year, make, model, VIN, and GVW.'",
                IsSystem = true,
                CreatedAt = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
