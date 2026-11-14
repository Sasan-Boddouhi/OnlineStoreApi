-- =====================================================================
-- Batch 52 (OPS-01): least-privilege SQL Server logins for OnlineStoreApi
-- =====================================================================
-- Run as sa, once on the first deployment and again whenever a password
-- is rotated. Safe to re-run: every step is "create if missing, otherwise
-- bring up to date".
--
--   docker compose -f docker-compose.prod.yml --profile setup run --rm db-init
--
-- sqlcmd scripting variables (taken from environment variables of the
-- db-init container, or from -v NAME=value):
--   DB_NAME                application database (ShopDB)
--   APP_DB_USER            login used by the API at run time
--   APP_DB_PASSWORD
--   MIGRATION_DB_USER      login used only by the one-shot migrator
--   MIGRATION_DB_PASSWORD
--
-- Passwords are pasted into the script text by sqlcmd, so they must not
-- contain a single quote ('), and because they also go into a connection
-- string they must not contain a semicolon (;). See docs/deployment.md.
--
-- Result:
--   * APP login       -> db_datareader, db_datawriter, EXECUTE; no DDL,
--                        no db_owner, no server role. The API cannot change
--                        the schema.
--   * MIGRATION login -> db_owner of DB_NAME only (EF Core migrations create,
--                        alter and drop tables, indexes and constraints and
--                        move data). It has no server-level role.
--   * Both logins default to DB_NAME. sa stays for the container health
--     check, backups and this script only.
-- =====================================================================
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ---------------------------------------------------------------------
-- 1. Input checks
-- ---------------------------------------------------------------------
DECLARE @db sysname = N'$(DB_NAME)';
DECLARE @appUser sysname = N'$(APP_DB_USER)';
DECLARE @appPassword nvarchar(128) = N'$(APP_DB_PASSWORD)';
DECLARE @migUser sysname = N'$(MIGRATION_DB_USER)';
DECLARE @migPassword nvarchar(128) = N'$(MIGRATION_DB_PASSWORD)';

IF LEN(@db) = 0 OR LEN(@appUser) = 0 OR LEN(@migUser) = 0
    THROW 50001, N'DB_NAME, APP_DB_USER and MIGRATION_DB_USER must not be empty.', 1;

IF @appUser = @migUser
    THROW 50002, N'APP_DB_USER and MIGRATION_DB_USER must be two different logins.', 1;

IF @appUser = N'sa' OR @migUser = N'sa'
    THROW 50003, N'Neither application login may be sa.', 1;

IF LEN(@appPassword) < 16 OR LEN(@migPassword) < 16
    THROW 50004, N'APP_DB_PASSWORD and MIGRATION_DB_PASSWORD must be at least 16 characters.', 1;

IF @appPassword = @migPassword
    THROW 50005, N'APP_DB_PASSWORD and MIGRATION_DB_PASSWORD must differ.', 1;

IF CHARINDEX(N';', @appPassword) > 0 OR CHARINDEX(N';', @migPassword) > 0
    THROW 50006, N'Passwords must not contain a semicolon (they are used in a connection string).', 1;
GO

-- ---------------------------------------------------------------------
-- 2. Database (an existing database is left untouched)
-- ---------------------------------------------------------------------
IF DB_ID(N'$(DB_NAME)') IS NULL
BEGIN
    DECLARE @createDb nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(N'$(DB_NAME)') + N';';
    EXEC (@createDb);
    PRINT N'Created database $(DB_NAME).';
END
ELSE
    PRINT N'Database $(DB_NAME) already exists.';
GO

-- ---------------------------------------------------------------------
-- 3. Server logins: create, or rotate the password of an existing one
-- ---------------------------------------------------------------------
DECLARE @logins TABLE (Name sysname NOT NULL, Pwd nvarchar(128) NOT NULL);
INSERT INTO @logins (Name, Pwd) VALUES
    (N'$(APP_DB_USER)', N'$(APP_DB_PASSWORD)'),
    (N'$(MIGRATION_DB_USER)', N'$(MIGRATION_DB_PASSWORD)');

DECLARE @name sysname, @pwd nvarchar(128), @sql nvarchar(max);
DECLARE login_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name, Pwd FROM @logins;
OPEN login_cursor;
FETCH NEXT FROM login_cursor INTO @name, @pwd;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @name AND type = 'S')
    BEGIN
        SET @sql = N'CREATE LOGIN ' + QUOTENAME(@name)
                 + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''')
                 + N', DEFAULT_DATABASE = ' + QUOTENAME(N'$(DB_NAME)')
                 + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
        EXEC (@sql);
        PRINT N'Created login ' + @name + N'.';
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN ' + QUOTENAME(@name)
                 + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''')
                 + N', DEFAULT_DATABASE = ' + QUOTENAME(N'$(DB_NAME)')
                 + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF; '
                 + N'ALTER LOGIN ' + QUOTENAME(@name) + N' ENABLE;';
        EXEC (@sql);
        PRINT N'Updated login ' + @name + N' (password set, enabled).';
    END;

    -- Neither login may hold a server role (for example sysadmin).
    IF IS_SRVROLEMEMBER(N'sysadmin', @name) = 1
    BEGIN
        SET @sql = N'ALTER SERVER ROLE sysadmin DROP MEMBER ' + QUOTENAME(@name) + N';';
        EXEC (@sql);
        PRINT N'Removed ' + @name + N' from sysadmin.';
    END;

    FETCH NEXT FROM login_cursor INTO @name, @pwd;
END;
CLOSE login_cursor;
DEALLOCATE login_cursor;
GO

-- ---------------------------------------------------------------------
-- 4. Database users and permissions
-- ---------------------------------------------------------------------
USE [$(DB_NAME)];
GO

-- Application login: read, write, execute. Nothing else.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(APP_DB_USER)')
    CREATE USER [$(APP_DB_USER)] FOR LOGIN [$(APP_DB_USER)];
ELSE
    -- Re-maps the user after a restore from another server (orphaned user).
    ALTER USER [$(APP_DB_USER)] WITH LOGIN = [$(APP_DB_USER)];

ALTER ROLE db_datareader ADD MEMBER [$(APP_DB_USER)];
ALTER ROLE db_datawriter ADD MEMBER [$(APP_DB_USER)];
GRANT EXECUTE TO [$(APP_DB_USER)];

-- Take away anything broader that an earlier manual setup may have granted.
IF IS_ROLEMEMBER(N'db_owner', N'$(APP_DB_USER)') = 1
    ALTER ROLE db_owner DROP MEMBER [$(APP_DB_USER)];
IF IS_ROLEMEMBER(N'db_ddladmin', N'$(APP_DB_USER)') = 1
    ALTER ROLE db_ddladmin DROP MEMBER [$(APP_DB_USER)];
IF IS_ROLEMEMBER(N'db_securityadmin', N'$(APP_DB_USER)') = 1
    ALTER ROLE db_securityadmin DROP MEMBER [$(APP_DB_USER)];
IF IS_ROLEMEMBER(N'db_accessadmin', N'$(APP_DB_USER)') = 1
    ALTER ROLE db_accessadmin DROP MEMBER [$(APP_DB_USER)];
GO

-- Migration login: owner of this database only (schema changes).
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(MIGRATION_DB_USER)')
    CREATE USER [$(MIGRATION_DB_USER)] FOR LOGIN [$(MIGRATION_DB_USER)];
ELSE
    ALTER USER [$(MIGRATION_DB_USER)] WITH LOGIN = [$(MIGRATION_DB_USER)];

ALTER ROLE db_owner ADD MEMBER [$(MIGRATION_DB_USER)];
GO

-- ---------------------------------------------------------------------
-- 5. Report (no passwords)
-- ---------------------------------------------------------------------
SELECT dp.name AS database_user,
       r.name  AS database_role
FROM sys.database_role_members AS rm
JOIN sys.database_principals AS r  ON r.principal_id  = rm.role_principal_id
JOIN sys.database_principals AS dp ON dp.principal_id = rm.member_principal_id
WHERE dp.name IN (N'$(APP_DB_USER)', N'$(MIGRATION_DB_USER)')
ORDER BY dp.name, r.name;

SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS sql_server_edition,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS product_version;
GO
