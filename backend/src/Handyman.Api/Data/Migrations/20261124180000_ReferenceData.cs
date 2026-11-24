using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Handyman.Api.Data.Migrations
{
    /// <summary>
    /// US-0018: trades, the placeholder Belgrade area tree and the placeholder store VRACAR01 exist in every
    /// environment (Production included), independent of the dev seeder. Data only, no schema change, so the
    /// model snapshot is unchanged.
    /// Rows get stable explicit ids (the identity columns accept them); the identity sequences are then moved
    /// to max(id) so later inserts do not collide. Safe on a database that already has data: a row that exists
    /// already (same name, kind and parent / same referral code) is kept as it is, and a row whose stable id is
    /// taken by an unrelated row gets the next free id instead. EF runs each migration once, so restarts and repeated "migrate" calls change nothing.
    /// All of this is placeholder data until the pilot city and store are decided (workflow.md section 11).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261124180000_ReferenceData")]
    public partial class ReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each row: keep it if it exists already; else insert with its stable id when that id is free;
            // else (the id is taken by an unrelated row) insert with the next free id. Then fix the sequences.
            migrationBuilder.Sql(@"
DO $ref$
DECLARE
  r record;
  parent int;
  existing_id int;
  ref_ids int[] := ARRAY[]::int[];
BEGIN
  FOR r IN SELECT * FROM (VALUES
      (1, 'Appliance repair'), (2, 'Electrical'), (3, 'Plumbing'),
      (4, 'Heating & AC'), (5, 'Painting'), (6, 'Carpentry')) AS v(id, name) LOOP
    IF NOT EXISTS (SELECT 1 FROM app.trades WHERE name = r.name) THEN
      IF EXISTS (SELECT 1 FROM app.trades WHERE id = r.id) THEN
        r.id := (SELECT max(id) FROM app.trades) + 1;
      END IF;
      INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE VALUES (r.id, r.name);
    END IF;
  END LOOP;

  -- Areas: parents are resolved through the ids already settled in this loop (ref_ids), never by name alone,
  -- so duplicate names elsewhere in app.areas cannot make a lookup return more than one row. A row that exists
  -- already under the resolved parent is adopted (lowest id wins when there are several).
  FOR r IN SELECT * FROM (VALUES
      (1,  'Beograd',         'City',         NULL),
      (2,  'Vračar',          'Municipality', 1),
      (3,  'Novi Beograd',    'Municipality', 1),
      (4,  'Zvezdara',        'Municipality', 1),
      (5,  'Crveni krst',     'Neighborhood', 2),
      (6,  'Čubura',          'Neighborhood', 2),
      (7,  'Englezovac',      'Neighborhood', 2),
      (8,  'Kalenić',         'Neighborhood', 2),
      (9,  'Krunski venac',   'Neighborhood', 2),
      (10, 'Neimar',          'Neighborhood', 2),
      (11, 'Bežanijska kosa', 'Neighborhood', 3),
      (12, 'Fontana',         'Neighborhood', 3),
      (13, 'Ledine',          'Neighborhood', 3),
      (14, 'Paviljoni',       'Neighborhood', 3),
      (15, 'Tošin bunar',     'Neighborhood', 3),
      (16, 'Cvetkova pijaca', 'Neighborhood', 4),
      (17, 'Đeram',           'Neighborhood', 4),
      (18, 'Konjarnik',       'Neighborhood', 4),
      (19, 'Mali Mokri Lug',  'Neighborhood', 4),
      (20, 'Mirijevo',        'Neighborhood', 4)) AS v(id, name, kind, parent_ref) ORDER BY v.id LOOP
    parent := ref_ids[r.parent_ref];
    existing_id := (SELECT a.id FROM app.areas a
                    WHERE a.kind = r.kind AND a.name = r.name AND a.parent_id IS NOT DISTINCT FROM parent
                    ORDER BY a.id LIMIT 1);
    IF existing_id IS NULL THEN
      IF EXISTS (SELECT 1 FROM app.areas WHERE id = r.id) THEN
        existing_id := (SELECT max(id) FROM app.areas) + 1;
      ELSE
        existing_id := r.id;
      END IF;
      INSERT INTO app.areas (id, name, kind, parent_id) OVERRIDING SYSTEM VALUE VALUES (existing_id, r.name, r.kind, parent);
    END IF;
    ref_ids[r.id] := existing_id;
  END LOOP;

  -- Same expression as ux_stores_referral_code_ci, so an existing code in any spelling is respected.
  IF NOT EXISTS (SELECT 1 FROM app.stores WHERE lower(regexp_replace(referral_code, '[\s ]+', '', 'g')) = 'vracar01') THEN
    INSERT INTO app.stores (id, name, area_id, referral_code) OVERRIDING SYSTEM VALUE
    VALUES (
      CASE WHEN EXISTS (SELECT 1 FROM app.stores WHERE id = 1) THEN (SELECT max(id) FROM app.stores) + 1 ELSE 1 END,
      'Home Center Vračar',
      ref_ids[6],
      'VRACAR01');
  END IF;

  PERFORM setval(pg_get_serial_sequence('app.trades', 'id'), GREATEST((SELECT max(id) FROM app.trades), 1));
  PERFORM setval(pg_get_serial_sequence('app.areas', 'id'), GREATEST((SELECT max(id) FROM app.areas), 1));
  PERFORM setval(pg_get_serial_sequence('app.stores', 'id'), GREATEST((SELECT max(id) FROM app.stores), 1));
END
$ref$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes only the rows this migration added (by id, kind and name) and only when nothing refers to them.
            migrationBuilder.Sql(@"
DELETE FROM app.stores s WHERE s.id = 1 AND s.name = 'Home Center Vračar' AND s.referral_code = 'VRACAR01'
  AND NOT EXISTS (SELECT 1 FROM app.jobs j WHERE j.store_id = s.id);
DELETE FROM app.areas a WHERE a.id BETWEEN 5 AND 20 AND a.kind = 'Neighborhood'
  AND NOT EXISTS (SELECT 1 FROM app.stores s WHERE s.area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.jobs j WHERE j.area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.users u WHERE u.home_area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.worker_areas w WHERE w.area_id = a.id);
DELETE FROM app.areas a WHERE a.id BETWEEN 2 AND 4 AND a.kind = 'Municipality'
  AND NOT EXISTS (SELECT 1 FROM app.areas c WHERE c.parent_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.stores s WHERE s.area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.jobs j WHERE j.area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.users u WHERE u.home_area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.worker_areas w WHERE w.area_id = a.id);
DELETE FROM app.areas a WHERE a.id = 1 AND a.kind = 'City' AND a.name = 'Beograd'
  AND NOT EXISTS (SELECT 1 FROM app.areas c WHERE c.parent_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.stores s WHERE s.area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.jobs j WHERE j.area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.users u WHERE u.home_area_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM app.worker_areas w WHERE w.area_id = a.id);
DELETE FROM app.trades t WHERE t.id BETWEEN 1 AND 6
  AND t.name IN ('Appliance repair', 'Electrical', 'Plumbing', 'Heating & AC', 'Painting', 'Carpentry')
  AND NOT EXISTS (SELECT 1 FROM app.jobs j WHERE j.trade_id = t.id)
  AND NOT EXISTS (SELECT 1 FROM app.worker_trades w WHERE w.trade_id = t.id);
");
        }
    }
}
