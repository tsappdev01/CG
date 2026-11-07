/*
    Adds the standard department list.

    Re-runnable: each row is guarded on its code, so running this twice adds nothing the second
    time and a list that has been added to by hand is left alone. Nothing is updated and nothing is
    deactivated -- a department already there keeps the name it has, because someone may have
    renamed it on purpose.

    Codes are matched case-insensitively, which is how SQL Server compares them under the usual
    collation anyway: this is what stops "HR" being inserted a second time as "hr".
*/
SET NOCOUNT ON;

DECLARE @Departments TABLE (Code nvarchar(20) NOT NULL, Name nvarchar(120) NOT NULL);

INSERT INTO @Departments (Code, Name) VALUES
    (N'CEO',   N'Office of CEO'),
    (N'FIN',   N'Finance'),
    (N'HR',    N'Human Resources'),
    (N'IT',    N'Information Technology'),
    (N'LEG',   N'Legal'),
    (N'COM',   N'Compliance'),
    (N'IA',    N'Internal Audit'),
    (N'CG',    N'Corporate Governance'),
    (N'IR',    N'Investor Relations'),
    (N'PM',    N'Property Management'),
    (N'AM',    N'Asset Management'),
    (N'DEV',   N'Real Estate Development'),
    (N'PRJ',   N'Projects'),
    (N'ENG',   N'Engineering'),
    (N'FM',    N'Facilities Management'),
    (N'LS',    N'Leasing'),
    (N'SAL',   N'Sales'),
    (N'MKT',   N'Marketing'),
    (N'PROC',  N'Procurement'),
    (N'RISK',  N'Risk Management'),
    (N'STR',   N'Strategy and Planning'),
    (N'ADMIN', N'Administration'),
    (N'COMM',  N'Corporate Communications');

DECLARE @Before int = (SELECT COUNT(*) FROM dbo.Departments);

INSERT INTO dbo.Departments (Code, Name, Active)
SELECT d.Code, d.Name, 1
FROM   @Departments AS d
WHERE  NOT EXISTS (SELECT 1 FROM dbo.Departments AS existing WHERE existing.Code = d.Code);

DECLARE @Added int = @@ROWCOUNT;
DECLARE @After int = (SELECT COUNT(*) FROM dbo.Departments);

PRINT 'Departments before : ' + CAST(@Before AS varchar(12));
PRINT 'Added              : ' + CAST(@Added AS varchar(12));
PRINT 'Departments now    : ' + CAST(@After AS varchar(12));

-- Anything in the list that was already there, under a different name, so a mismatch is seen
-- rather than silently kept.
SELECT   d.Code,
         ProposedName = d.Name,
         CurrentName  = existing.Name,
         existing.Active
FROM     @Departments AS d
         INNER JOIN dbo.Departments AS existing ON existing.Code = d.Code
WHERE    existing.Name <> d.Name
ORDER BY d.Code;
