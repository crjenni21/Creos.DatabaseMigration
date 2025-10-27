if not exists (
		select 1 
		from sys.tables t 
		inner join sys.schemas s on t.schema_id = s.schema_id
			and s.name = N'dbo'
		where t.name = N'testchris') 	
	create table dbo.testchris (         some_column1 varchar(64) not null     )