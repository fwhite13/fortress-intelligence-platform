using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamOs.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverageLineToSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Phase 1: Add columns ──────────────────────────────────────────────

            migrationBuilder.Sql(
                "ALTER TABLE submissions ADD COLUMN CoverageLine VARCHAR(50) NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE submissions ADD COLUMN LineStatus TINYINT NOT NULL DEFAULT 0;");

            migrationBuilder.Sql(
                "ALTER TABLE quotes ADD COLUMN CoverageLine VARCHAR(50) NULL;");

            // ── Phase 2: Data migration ───────────────────────────────────────────

            // Step 1+2 combined: queue drain check (aborts if uploading/processing)
            // then split CoverageTypes CSV into one row per carrier × line.
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS SplitSubmissionLines;");

            migrationBuilder.Sql(@"
CREATE PROCEDURE SplitSubmissionLines()
BEGIN
    -- All DECLAREs must come first in MySQL
    DECLARE v_in_flight INT;
    DECLARE done INT DEFAULT FALSE;
    DECLARE v_id CHAR(36);
    DECLARE v_opp_id CHAR(36);
    DECLARE v_carrier VARCHAR(200);
    DECLARE v_coverage_types VARCHAR(200);
    DECLARE v_status INT;
    DECLARE v_submitted_at DATETIME;
    DECLARE v_responded_at DATETIME;
    DECLARE v_notes LONGTEXT;
    DECLARE v_created_at DATETIME;
    DECLARE v_updated_at DATETIME;
    DECLARE v_line VARCHAR(50);
    DECLARE v_first_line VARCHAR(50);
    DECLARE v_remaining VARCHAR(200);
    DECLARE v_comma_pos INT;
    DECLARE v_new_id CHAR(36);

    DECLARE cur CURSOR FOR
        SELECT Id, OpportunityId, CarrierName, CoverageTypes, Status,
               SubmittedAt, RespondedAt, Notes, CreatedAt, UpdatedAt
        FROM submissions
        WHERE CoverageLine IS NULL;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = TRUE;

    -- Step 1: Queue drain check — abort if any uploads are in progress
    SET v_in_flight = (SELECT COUNT(*) FROM submissions WHERE Status IN (5, 6));
    IF v_in_flight > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Migration aborted: submissions in Uploading/Processing state. Drain scraper queue first.';
    END IF;

    -- Step 2: Split CoverageTypes CSV — one row per carrier x line

    OPEN cur;
    read_loop: LOOP
        FETCH cur INTO v_id, v_opp_id, v_carrier, v_coverage_types, v_status,
                       v_submitted_at, v_responded_at, v_notes, v_created_at, v_updated_at;
        IF done THEN
            LEAVE read_loop;
        END IF;

        -- Handle NULL or empty CoverageTypes
        IF v_coverage_types IS NULL OR TRIM(v_coverage_types) = '' THEN
            UPDATE submissions SET CoverageLine = 'Unknown' WHERE Id = v_id;
        ELSE
            SET v_remaining = TRIM(v_coverage_types);
            SET v_first_line = NULL;

            WHILE LENGTH(v_remaining) > 0 DO
                SET v_comma_pos = LOCATE(',', v_remaining);
                IF v_comma_pos > 0 THEN
                    SET v_line = TRIM(SUBSTRING(v_remaining, 1, v_comma_pos - 1));
                    SET v_remaining = TRIM(SUBSTRING(v_remaining, v_comma_pos + 1));
                ELSE
                    SET v_line = TRIM(v_remaining);
                    SET v_remaining = '';
                END IF;

                IF v_line != '' THEN
                    IF v_first_line IS NULL THEN
                        -- First line: UPDATE the original row in-place
                        SET v_first_line = v_line;
                        UPDATE submissions SET CoverageLine = v_line WHERE Id = v_id;
                    ELSE
                        -- Subsequent lines: INSERT new sibling rows
                        SET v_new_id = UUID();
                        INSERT INTO submissions (
                            Id, OpportunityId, CarrierName, CoverageTypes, CoverageLine,
                            Status, LineStatus, SubmittedAt, RespondedAt, Notes,
                            CreatedAt, UpdatedAt, fortress_request_id, scraper_error, QuoteResultJson
                        ) VALUES (
                            v_new_id, v_opp_id, v_carrier, v_coverage_types, v_line,
                            0, 0, v_submitted_at, NULL, NULL,
                            NOW(), NOW(), NULL, NULL, NULL
                        );
                    END IF;
                END IF;
            END WHILE;
        END IF;
    END LOOP;
    CLOSE cur;
END");

            migrationBuilder.Sql("CALL SplitSubmissionLines();");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS SplitSubmissionLines;");

            // Step 3: Re-point quotes.SubmissionId to matching per-line submission rows.
            // FK_CHECKS disabled for duration of the UPDATE to avoid constraint violations.
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS RePointQuoteSubmissions;");

            migrationBuilder.Sql(@"
CREATE PROCEDURE RePointQuoteSubmissions()
BEGIN
    DECLARE v_null_fks INT;

    SET FOREIGN_KEY_CHECKS = 0;

    UPDATE quotes q
    INNER JOIN (
        SELECT s.Id AS new_sub_id, s.OpportunityId, s.CarrierName,
               ROW_NUMBER() OVER (PARTITION BY s.OpportunityId, s.CarrierName ORDER BY s.Id) AS rn
        FROM submissions s
        WHERE s.CoverageLine IS NOT NULL
    ) sub_map ON sub_map.OpportunityId = q.OpportunityId
             AND sub_map.CarrierName    = q.CarrierName
             AND sub_map.rn             = 1
    SET q.SubmissionId = sub_map.new_sub_id;

    SET FOREIGN_KEY_CHECKS = 1;

    -- Post-migration assertion: every quote must have a non-null SubmissionId
    SET v_null_fks = (SELECT COUNT(*) FROM quotes WHERE SubmissionId IS NULL);
    IF v_null_fks > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Post-migration assertion failed: quotes with null SubmissionId found';
    END IF;
END");

            migrationBuilder.Sql("CALL RePointQuoteSubmissions();");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS RePointQuoteSubmissions;");

            // Step 4: Finalize — make CoverageLine NOT NULL and add unique constraint
            migrationBuilder.Sql(
                "ALTER TABLE submissions MODIFY COLUMN CoverageLine VARCHAR(50) NOT NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE submissions ADD UNIQUE KEY uq_submission_carrier_line (OpportunityId, CarrierName, CoverageLine);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE submissions DROP INDEX uq_submission_carrier_line;");

            migrationBuilder.Sql(
                "ALTER TABLE submissions DROP COLUMN CoverageLine;");

            migrationBuilder.Sql(
                "ALTER TABLE submissions DROP COLUMN LineStatus;");

            migrationBuilder.Sql(
                "ALTER TABLE quotes DROP COLUMN CoverageLine;");
        }
    }
}
