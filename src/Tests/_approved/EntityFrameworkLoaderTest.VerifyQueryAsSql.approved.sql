.param set @name_startswith 'Mic%'
.param set @p 10

SELECT "c"."Id", "c"."Name", "c"."Website"
FROM "Company" AS "c"
WHERE "c"."Name" LIKE @name_startswith ESCAPE '\'
LIMIT @p
