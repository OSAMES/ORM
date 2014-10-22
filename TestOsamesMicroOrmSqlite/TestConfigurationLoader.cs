﻿/*
This file is part of OSAMES Micro ORM.
Copyright 2014 OSAMES

OSAMES Micro ORM is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

OSAMES Micro ORM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with OSAMES Micro ORM.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OsamesMicroOrm;
using OsamesMicroOrm.Configuration;
using OsamesMicroOrm.Configuration.Tweak;

namespace TestOsamesMicroOrmSqlite
{
    [TestClass]
   public class TestConfigurationLoader : OsamesMicroOrmSqliteTest
    {

        /// <summary>
        /// Pour ce projet de TU il y a seulement un provider Sqlite définis dans App.Config.
        /// </summary>
        [TestMethod]
        [ExcludeFromCodeCoverage]
        [Owner("Barbara Post")]
        [TestCategory("Configuration")]
        [TestCategory("Sql provider search")]
        public void TestFindInProviderFactoryClasses()
        {
            ConfigurationLoader tempo = ConfigurationLoader.Instance;

            Assert.IsFalse(ConfigurationLoader.FindInProviderFactoryClasses("some.provider"));
            Assert.IsTrue(ConfigurationLoader.FindInProviderFactoryClasses("System.Data.SQLite"));
            Assert.IsTrue(ConfigurationLoader.FindInProviderFactoryClasses("System.Data.SqlClient"));

        }

        /// <summary>
        /// Load of correct configuration file.
        /// Assertions on formatted string related to database access that was passed to DbHelper.
        /// TU for SqLite Databases
        /// </summary>
        [TestMethod]
        [ExcludeFromCodeCoverage]
        [Owner("Benjamin Nolmans")]
        [TestCategory("Configuration")]
        [TestCategory("SqLite")]
        public void TestConfigurationLoaderAssertOnSqLiteDatabaseParameters()
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ""));
                ConfigurationLoader tempo = ConfigurationLoader.Instance;

                Assert.AreEqual(string.Format("Data Source={0}{1}", AppDomain.CurrentDomain.BaseDirectory, @"\DB\Chinook_Sqlite.sqlite;Version=3;UTF8Encoding=True;"), DbManager.ConnectionString);
                Assert.AreEqual(@"System.Data.SQLite", DbManager.ProviderName);
        }

        [TestMethod]
        [ExcludeFromCodeCoverage]
        [Owner("Barbara Post")]
        [TestCategory("XML")]
        [TestCategory("Configuration")]
        [TestCategory("SqLite")]
        public void TestLoadProviderSpecificInformation()
        {
                ConfigurationLoader tempo = ConfigurationLoader.Instance;

                Assert.AreEqual("System.Data.SQLite", DbManager.ProviderName, "Nom du provider incorrect après détermination depuis le fichier des AppSettings et celui des connection strings");
                Assert.AreEqual("[", ConfigurationLoader.StartFieldEncloser, "Start field encloser incorrect après détermination depuis le fichier des AppSettings et celui des templates XML");
                Assert.AreEqual("]", ConfigurationLoader.EndFieldEncloser, "End field encloser incorrect après détermination depuis le fichier des AppSettings et celui des templates XML");

                Assert.AreEqual("select last_insert_rowid();", DbManager.SelectLastInsertIdCommandText, "Texte pour 'select last insert id' incorrect après détermination depuis le fichier des AppSettings et celui des templates XML");
        }
    }
}
