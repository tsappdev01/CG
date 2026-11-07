/*
    Is the database carrying the share-entry stored procedure the site expects?

    The site calls dbo.usp_InsiderDeclarationShareEntry_Insert with @IsSelf and @Relationship.
    An earlier version took @HolderKind instead. If this server still has that one, every save of
    an Insider Trading declaration fails and rolls back -- which looks exactly like the form
    losing what was typed into it.

    Run against VMS's CGS database. Expect nine parameters plus @NewId, including @IsSelf and
    @Relationship, and no @HolderKind.
*/
SELECT  p.name              AS Parameter,
        TYPE_NAME(p.user_type_id) AS DataType,
        p.is_output         AS IsOutput
FROM    sys.parameters AS p
WHERE   p.object_id = OBJECT_ID('dbo.usp_InsiderDeclarationShareEntry_Insert')
ORDER BY p.parameter_id;

-- Nothing at all back from that means the procedure is missing entirely.
SELECT  CASE
            WHEN OBJECT_ID('dbo.usp_InsiderDeclarationShareEntry_Insert') IS NULL
                THEN 'MISSING - run scripts/stored-procedures.sql'
            WHEN EXISTS (SELECT 1 FROM sys.parameters
                         WHERE object_id = OBJECT_ID('dbo.usp_InsiderDeclarationShareEntry_Insert')
                           AND name = '@IsSelf')
                THEN 'Current'
            ELSE 'OUT OF DATE - run scripts/stored-procedures.sql'
        END AS ShareEntryInsertProc;

-- And the table the procedure writes into, for the same reason.
SELECT  c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS DataType
FROM    sys.columns AS c
WHERE   c.object_id = OBJECT_ID('dbo.InsiderDeclarationShareEntries')
ORDER BY c.column_id;
