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
  city_id int;
  parent int;
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

  FOR r IN SELECT * FROM (VALUES
      (1,  'Beograd',         'City',         NULL),
      (2,  'Vračar',          'Municipality', 'Beograd'),
      (3,  'Novi Beograd',    'Municipality', 'Beograd'),
      (4,  'Zvezdara',        'Municipality', 'Beograd'),
      (5,  'Crveni krst',     'Neighborhood', 'Vračar'),
      (6,  'Čubura',          'Neighborhood', 'Vračar'),
      (7,  'Englezovac',      'Neighborhood', 'Vračar'),
      (8,  'Kalenić',         'Neighborhood', 'Vračar'),
      (9,  'Krunski venac',   'Neighborhood', 'Vračar'),
      (10, 'Neimar',          'Neighborhood', 'Vračar'),
      (11, 'Bežanijska kosa', 'Neighborhood', 'Novi Beograd'),
      (12, 'Fontana',         'Neighborhood', 'Novi Beograd'),
      (13, 'Ledine',          'Neighborhood', 'Novi Beograd'),
      (14, 'Paviljoni',       'Neighborhood', 'Novi Beograd'),
      (15, 'Tošin bunar',     'Neighborhood', 'Novi Beograd'),
      (16, 'Cvetkova pijaca', 'Neighborhood', 'Zvezdara'),
      (17, 'Đeram',           'Neighborhood', 'Zvezdara'),
      (18, 'Konjarnik',       'Neighborhood', 'Zvezdara'),
      (19, 'Mali Mokri Lug',  'Neighborhood', 'Zvezdara'),
      (20, 'Mirijevo',        'Neighborhood', 'Zvezdara')) AS v(id, name, kind, parent_name) ORDER BY v.id LOOP
    parent := CASE r.kind
      WHEN 'Municipality' THEN (SELECT id FROM app.areas WHERE kind = 'City' AND name = r.parent_name)
      WHEN 'Neighborhood' THEN (SELECT id FROM app.areas WHERE kind = 'Municipality' AND name = r.parent_name)
    END;
    IF NOT EXISTS (SELECT 1 FROM app.areas WHERE kind = r.kind AND name = r.name AND parent_id IS NOT DISTINCT FROM parent) THEN
      IF EXISTS (SELECT 1 FROM app.areas WHERE id = r.id) THEN
        r.id := (SELECT max(id) FROM app.areas) + 1;
      END IF;
      INSERT INTO app.areas (id, name, kind, parent_id) OVERRIDING SYSTEM VALUE VALUES (r.id, r.name, r.kind, parent);
    END IF;
  END LOOP;

  -- Same expression as ux_stores_referral_code_ci, so an existing code in any spelling is respected.
  IF NOT EXISTS (SELECT 1 FROM app.stores WHERE lower(regexp_replace(referral_code, '[\s ]+', '', 'g')) = 'vracar01') THEN
    INSERT INTO app.stores (id, name, area_id, referral_code) OVERRIDING SYSTEM VALUE
    VALUES (
      CASE WHEN EXISTS (SELECT 1 FROM app.stores WHERE id = 1) THEN (SELECT max(id) FROM app.stores) + 1 ELSE 1 END,
      'Home Center Vračar',
      (SELECT n.id FROM app.areas n JOIN app.areas m ON m.id = n.parent_id AND m.kind = 'Municipality' AND m.name = 'Vračar'
        WHERE n.kind = 'Neighborhood' AND n.name = 'Čubura'),
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
