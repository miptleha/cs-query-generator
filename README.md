# csharp-query-generator
Generation of sql scripts (creation and insertion) from cs-classes. Implemented for Oracle syntax.

Open QueryGenerator.sln in Visual Studio. 
Open Program.cs. It contains automatic tests, that creates sql-database and perform insertion of test data.
Set connection to Oracle in App.config
Run application: it creates tables for 2 test classes: AF/TestClass1.cs and AF/TestClass2.cs
See console or bin/Debug/QueryGenerator.log for errors and output messages
All generated code are stored in bin folder
