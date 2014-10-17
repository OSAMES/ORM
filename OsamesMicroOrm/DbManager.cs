/*
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using OsamesMicroOrm.Logging;

namespace OsamesMicroOrm
{
    /// <summary>
    /// Generic ADO.NET level, multi thread class, that deals with database providers and database query execution.
    /// </summary>
    public class DbManager
    {

        #region DECLARATIONS

        /// <summary>
        /// Current DB provider factory.
        /// </summary>
        internal DbProviderFactory DbProviderFactory;

        /// <summary>
        /// First created connection, to be used when pool is exhausted when pooling is active.
        /// </summary>
        private DbConnection BackupConnection;

        /// <summary>
        /// Connection string that is set/checked by ConnectionString property.
        /// </summary>
        private static string ConnectionStringField;
        /// <summary>
        /// Invariant provider name that is set/checked by ProviderName property.
        /// </summary>
        private static string ProviderInvariantName;
        /// <summary>
        /// Provider specific SQL code for "select last insert id" that is set/checked by SelectLastInsertIdCommandText property.
        /// </summary>
        private static string SelectLastInsertIdCommandTextField;

        /// <summary>
        /// Singleton.
        /// </summary>
        private static DbManager Singleton;
        /// <summary>
        /// Lock object for singleton initialization.
        /// </summary>
        private static readonly object SingletonInitLockObject = new object();

        /// <summary>
        /// Lock object for using backup connection.
        /// </summary>
        private static readonly object BackupConnectionUsageLockObject = new object();

        /// <summary>
        /// Singleton access, with singleton thread-safe initialization using dedicated lock object.
        /// </summary>
        public static DbManager Instance
        {
            get
            {
                lock (SingletonInitLockObject)
                {
                    return Singleton ?? (Singleton = new DbManager());
                }
            }
        }

        /// <summary>
        /// Standard SQL provider specific connection string.
        /// Getter throws an exception if setter hasn't been called with a value.
        /// </summary>
        internal static string ConnectionString
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ConnectionStringField))
                {
                    Logger.Log(TraceEventType.Critical, "Connection string not set!");
                    throw new Exception("ConnectionString column not initialized, please set a value!");
                }
                return ConnectionStringField;
            }
            set { ConnectionStringField = value; }
        }

        /// <summary>
        /// Provider specific SQL query select instruction (= command text), to execute "Select Last Insert Id".
        /// It's necessary to define it because there is no ADO.NET generic way of retrieving last insert ID after a SQL update execution.
        /// </summary>
        internal static string SelectLastInsertIdCommandText
        {
            get
            {
                if (SelectLastInsertIdCommandTextField == null)
                {
                    Logger.Log(TraceEventType.Critical, "Select Last Insert Id Command Text not set!");
                    throw new Exception("SelectLastInsertIdCommandText column not initialized, please set a value!");
                }
                return SelectLastInsertIdCommandTextField;
            }
            set { SelectLastInsertIdCommandTextField = value; }
        }

        /// <summary>
        /// Database provider definition. ADO.NET provider invariant name.
        /// </summary>
        internal static string ProviderName
        {
            get
            {
                if (ProviderInvariantName == null)
                {
                    Logger.Log(TraceEventType.Critical, "Database provider not set!");
                    throw new Exception("ProviderName column not initialized, please set a value!");
                }
                return ProviderInvariantName;
            }
            set { ProviderInvariantName = value; }
        }

        #endregion

        #region STRUCTURES

        /// <summary>
        /// Representation of an ADO.NET parameter. Used same way as an ADO.NET parameter but without depending on System.Data namespace in user code.
        /// It means more code overhead but is fine to deal with list of complex objects rather than list of values.
        /// </summary>
        public struct Parameter
        {
            /// <summary>
            /// 
            /// </summary>
            public string ParamName;

            /// <summary>
            /// 
            /// </summary>
            public object ParamValue;

            /// <summary>
            /// 
            /// </summary>
            public ParameterDirection ParamDirection;

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="name_">Name</param>
            /// <param name="value_">Value</param>
            /// <param name="direction_">ADO.NET parameter direction</param>
            public Parameter(string name_, object value_, ParameterDirection direction_)
            {
                ParamName = name_;
                ParamValue = value_;
                ParamDirection = direction_;
            }
            /// <summary>
            /// Constructor with default "in" direction.
            /// </summary>
            /// <param name="name_">Name</param>
            /// <param name="value_">Value</param>
            public Parameter(string name_, object value_)
            {
                ParamName = name_;
                ParamValue = value_;
                ParamDirection = ParameterDirection.Input;
            }
        }

        #endregion

        #region CONSTRUCTOR

        /// <summary>
        /// Constructor called by singleton initialization. Tries to instantiate provider from its name.
        /// </summary>
        private DbManager()
        {
            DbProviderFactory = DbProviderFactories.GetFactory(ProviderName);
        }

        #endregion

        #region DESTRUCTOR

        /// <summary>
        /// Destructor. Sets internal static variables that could be linked to unmanaged resources to null.
        /// </summary>
        ~DbManager()
        {
            BackupConnection.Close();
            DbProviderFactory = null;
        }

        #endregion

        #region CONNECTIONS

        /// <summary>
        /// Try to get a new connection, usually from pool (may get backup connection in this case) or single connection.
        /// Opens the connection before returning it.
        /// May throw exception only when no connection at all can be opened.	
        /// </summary>
        public DbConnection CreateConnection()
        {
            try
            {
                System.Data.Common.DbConnection adoConnection = DbProviderFactory.CreateConnection();
                adoConnection.ConnectionString = ConnectionString;
                adoConnection.Open();
                // everything OK
                if (BackupConnection == null)
                {
                    // we just opened our first connection!
                    // Keep a reference to it and keep this backup connexion unused for now
                    BackupConnection = new DbConnection(adoConnection, true);
                    // Try to get a second connection and return it
                    return CreateConnection();
                }
                // Not the first connection
                DbConnection pooledConnection = new DbConnection(adoConnection, false);
                return pooledConnection;

            }
            catch (Exception ex)
            {
                // could not get a new connection !
                if (BackupConnection == null)
                {
                    // could not get any connection
                    Logger.Log(TraceEventType.Critical, ex);
                    throw new Exception("DbManager, CreateConnection: Connection could not be created! *** " + ex.Message + " *** . Look at detailed log for details");
                }
                // could not get a second connection
                // use backup connection
                // We may have to reopen it because we waited a long time before using it
                if (BackupConnection.State != ConnectionState.Open)
                {
                    BackupConnection.ConnectionString = ConnectionString;
                    BackupConnection.Open();
                }

                return BackupConnection;
            }

        }
        /// <summary>
        /// Fermeture d'une connexion et dispose/mise à null de l'objet.
        /// </summary>
        /// <param name="connexion_">connexion</param>
        /// <returns>Ne renvoie rien</returns>
        public void DisposeConnection(ref DbConnection connexion_)
        {
            if (connexion_ == null) return;

            connexion_.Close();
            connexion_ = null;
        }

        #endregion

        #region TRANSACTION

        /// <summary>
        /// Opens a transaction and returns it.
        /// </summary>
        public DbTransaction OpenTransaction(DbConnection connection_)
        {
            try
            {
                return connection_.BeginTransaction();

            }
            catch (InvalidOperationException ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw new Exception("OpenTransaction - " + ex.Message);
            }
        }

        /// <summary>
        /// Commits and closes a transaction.
        /// </summary>
        /// <param name="transaction_">Transaction to manage</param>
        public void CommitTransaction(DbTransaction transaction_)
        {
            try
            {
                if (transaction_ == null) return;

                transaction_.Commit();
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw new Exception("CommitTransaction - " + ex.Message);
            }
        }

        /// <summary>
        /// Rollbacks and closes a transaction.
        /// </summary>
        /// <param name="transaction_">Transaction to manage</param>
        public void RollbackTransaction(DbTransaction transaction_)
        {
            try
            {
                if (transaction_ == null) return;

                transaction_.Rollback();
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw new Exception("@HandleTransaction - " + ex.Message);
            }
        }

        #endregion

        #region COMMANDS

        #region PARAMETERLESS METHODS

        /// <summary>
        /// Initializes a DbCommand object with parameters and returns it ready for execution.
        /// </summary>
        /// <param name="connection_">Current connection</param>
        /// <param name="transaction_">When not null, transaction to assign to _command. OpenTransaction() should have been called first</param>
        /// <param name="cmdType_">Type of command (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        private DbCommand PrepareCommand(DbConnection connection_, DbTransaction transaction_, string cmdText_, CommandType cmdType_ = CommandType.Text)
        {
            System.Data.Common.DbCommand adoCommand = DbProviderFactory.CreateCommand();

            if (adoCommand == null)
            {
                throw new Exception("DbHelper, PrepareCommand: Command could not be created");
            }

            DbCommand command = new DbCommand(adoCommand) { Connection = connection_, CommandText = cmdText_, CommandType = cmdType_ };

            if (transaction_ != null)
                command.Transaction = transaction_;

            return command;
        }

        #endregion

        #region OBJECT BASED PARAMETER ARRAY

        /// <summary>
        /// Signature de PrepareCommand sous forme "générique". Le compilateur trouvera des signatures identiques mais avec un type donné pour cmdParams_.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="connection_"></param>
        /// <param name="transaction_"></param>
        /// <param name="cmdText_"></param>
        /// <param name="cmdParams_"></param>
        /// <param name="cmdType_"></param>
        /// <returns></returns>
        private DbCommand PrepareCommand<T>(DbConnection connection_, DbTransaction transaction_, string cmdText_, T cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            // TODO à supprimer ou pas selon le code résultant de la boucle sur les éléments
            throw new NotImplementedException("PrepareCommand() doit avoir une implémentation propre au type " + typeof(T));
        }
     

        /// <summary>
        /// Initializes a DbCommand object with parameters and returns it ready for execution.
        /// </summary>
        /// <param name="connection_">Current connection</param>
        /// <param name="transaction_">When not null, transaction to assign to _command. OpenTransaction() should have been called first</param>
        /// <param name="cmdType_">Type of command (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in multiple array format</param>
        private DbCommand PrepareCommand(DbConnection connection_, DbTransaction transaction_, string cmdText_, object[,] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            DbCommand command = PrepareCommand(connection_, transaction_, cmdText_, cmdType_);

            if (cmdParams_ != null)
                CreateDbParameters(command, cmdParams_);

            return command;
        }

        #endregion

        #region STRUCTURE BASED PARAMETER ARRAY

        /// <summary>
        /// Initializes a DbCommand object with parameters and returns it ready for execution.
        /// </summary>
        /// <param name="connection_">Current connection</param>
        /// <param name="transaction_">When not null, transaction to assign to _command. OpenTransaction() should have been called first</param>
        /// <param name="cmdType_">Type of command (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) as enumerable Parameter objects format</param>
        private DbCommand PrepareCommand(DbConnection connection_, DbTransaction transaction_, string cmdText_, IEnumerable<Parameter> cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            DbCommand command = PrepareCommand(connection_, transaction_, cmdText_, cmdType_);

            if (cmdParams_ != null)
                CreateDbParameters(command, cmdParams_);
            return command;
        }

        #endregion

        #region KEY VALUE PAIR BASED PARAMETER ARRAY

        /// <summary>
        /// Initializes a DbCommand object with parameters and returns it ready for execution.
        /// </summary>
        /// <param name="connection_">Current connection</param>
        /// <param name="transaction_">When not null, transaction to assign to _command. OpenTransaction() should have been called first</param>
        /// <param name="cmdType_">Type of command (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in list of key/value pair format</param>
        private DbCommand PrepareCommand(DbConnection connection_, DbTransaction transaction_, string cmdText_, List<KeyValuePair<string, object>> cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            DbCommand command = PrepareCommand(connection_, transaction_, cmdText_, cmdType_);

            if (cmdParams_ != null)
                CreateDbParameters(command, cmdParams_);
            return command;
        }

        #endregion

        #endregion

        #region PARAMETER METHODS

        #region OBJECT BASED

        /// <summary>
        /// Adds ADO.NET parameters to parameter DbCommand.
        /// Parameters are all input parameters.
        /// </summary>
        /// <param name="command_">DbCommand to add parameters to</param>
        /// <param name="adoParams_">ADO.NET parameters (name and value) in multiple array format</param>
        private void CreateDbParameters(DbCommand command_, object[,] adoParams_)
        {
            for (int i = 0; i < adoParams_.Length / 2; i++)
            {
                DbParameter dbParameter = command_.CreateParameter();
                dbParameter.ParameterName = adoParams_[i, 0].ToString();
                dbParameter.Value = adoParams_[i, 1];
                dbParameter.Direction = ParameterDirection.Input;
                command_.Parameters.Add(dbParameter);
            }
        }

        #endregion

        #region STRUCTURE BASED

        /// <summary>
        /// Adds ADO.NET parameters to parameter DbCommand.
        /// Parameters can be input or output parameters.
        /// </summary>
        /// <param name="command_">DbCommand to add parameters to</param>
        /// <param name="adoParams_">ADO.NET parameters (name and value) as enumerable Parameter objects format</param>
        private void CreateDbParameters(DbCommand command_, IEnumerable<Parameter> adoParams_)
        {
            foreach (Parameter oParam in adoParams_)
            {
                DbParameter dbParameter = command_.CreateParameter();
                dbParameter.ParameterName = oParam.ParamName;
                dbParameter.Value = oParam.ParamValue;
                dbParameter.Direction = oParam.ParamDirection;
                command_.Parameters.Add(dbParameter);
            }
        }

        #endregion

        #region KeyValuePair based

        /// <summary>
        /// Adds ADO.NET parameters to parameter DbCommand.
        /// Parameters are all input parameters.
        /// </summary>
        /// <param name="command_">DbCommand to add parameters to</param>
        /// <param name="adoParams_">ADO.NET parameters (name and value) as enumerable Parameter objects format</param>
        private void CreateDbParameters(DbCommand command_, IEnumerable<KeyValuePair<string, object>> adoParams_)
        {
            foreach (KeyValuePair<string, object> oParam in adoParams_)
            {
                DbParameter dbParameter = command_.CreateParameter();
                dbParameter.ParameterName = oParam.Key;
                dbParameter.Value = oParam.Value;
                dbParameter.Direction = ParameterDirection.Input;
                command_.Parameters.Add(dbParameter);
            }
        }

        #endregion

        #endregion

        #region EXECUTE METHODS

        #region PARAMETERLESS METHODS

        /// <summary>
        /// Executes an SQL statement which returns number of affected rows("non query command").
        /// </summary>
        /// <param name="lastInsertedRowId_">Last inserted row ID (long number)</param>
        /// <param name="cmdType_">Command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="transaction_">When not null, transaction to use</param>
        /// <returns>Number of affected rows</returns>
        public int ExecuteNonQuery(string cmdText_, out long lastInsertedRowId_, CommandType cmdType_ = CommandType.Text, DbTransaction transaction_ = null)
        {
            DbConnection dbConnection = null;
            try
            {
                // Utiliser la connexion de la transaction ou une nouvelle connexion
                dbConnection = transaction_ != null ? transaction_.Connection : CreateConnection();

                // on n'a pas de paramètres donc on passe null
                // pour l'écriture générique, on caste null en IEnumerable (on doit passer un type C#)
                if (!dbConnection.IsBackup)
                {
                    return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, (IEnumerable)null, out lastInsertedRowId_);
                }
                else
                {
                    lock (BackupConnectionUsageLockObject)
                    {
                        return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, (IEnumerable)null, out lastInsertedRowId_);
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw;
            }
            finally
            {
                if (transaction_ == null && dbConnection != null)
                {
                    // La connexion n'etait pas celle de la transaction
                    dbConnection.Close();
                    dbConnection.Dispose();
                }

            }

        }


        #endregion

        #region OBJECT BASED PARAMETER ARRAY

        /// <summary>
        /// Executes an SQL statement which returns number of  affected rows("non query command").
        /// </summary>
        /// <param name="lastInsertedRowId_">Last inserted row ID (long number)</param>
        /// <param name="cmdType_">Command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in multiple array format</param>
        /// <param name="transaction_">When not null, transaction to use</param>
        /// <returns>Number of affected rows</returns>
        public int ExecuteNonQuery(string cmdText_, object[,] cmdParams_, out long lastInsertedRowId_, CommandType cmdType_ = CommandType.Text, DbTransaction transaction_ = null)
        {
            DbConnection dbConnection = null;
            try
            {
                // Utiliser la connexion de la transaction ou une nouvelle connexion
                dbConnection = transaction_ != null ? transaction_.Connection : CreateConnection();

                // pour l'écriture générique, on caste object[,] en IEnumerable (on doit passer un type C#)
                if (!dbConnection.IsBackup)
                {
                    return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, cmdParams_, out lastInsertedRowId_);
                }
                else
                {
                    lock (BackupConnectionUsageLockObject)
                    {
                        return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, cmdParams_, out lastInsertedRowId_);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw;
            }
            finally
            {
                if (transaction_ == null && dbConnection != null)
                {
                    // La connexion n'etait pas celle de la transaction
                    dbConnection.Close();
                    dbConnection.Dispose();
                }

            }

        }


        #endregion

        #region STRUCTURE BASED PARAMETER ARRAY

        /// <summary>
        /// Executes an SQL statement which returns number of  affected rows("non query command").
        /// </summary>
        /// <param name="lastInsertedRowId_">Last inserted row ID (long number)</param>
        /// <param name="cmdType_">Command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in array of Parameter objects format</param>
        /// <param name="transaction_">When not null, transaction to use</param>
        /// <returns>Number of affected rows</returns>
        public int ExecuteNonQuery(string cmdText_, Parameter[] cmdParams_, out long lastInsertedRowId_, CommandType cmdType_ = CommandType.Text, DbTransaction transaction_ = null)
        {
            DbConnection dbConnection = null;
            try
            {
                // Utiliser la connexion de la transaction ou une nouvelle connexion
                dbConnection = transaction_ != null ? transaction_.Connection : CreateConnection();

                // pour l'écriture générique, on caste Parameter[] en IEnumerable (on doit passer un type C#)
                if (!dbConnection.IsBackup)
                {
                    return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, cmdParams_, out lastInsertedRowId_);
                }
                else
                {
                    lock (BackupConnectionUsageLockObject)
                    {
                        return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, cmdParams_, out lastInsertedRowId_);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw;
            }
            finally
            {
                if (transaction_ == null && dbConnection != null)
                {
                    // La connexion n'etait pas celle de la transaction
                    dbConnection.Close();
                    dbConnection.Dispose();
                }

            }
        }

        #endregion

        #region KEY VALUE PAIR BASED PARAMETER ARRAY

        /// <summary>
        /// Executes an SQL statement which returns number of  affected rows("non query command").
        /// </summary>
        /// <param name="lastInsertedRowId_">Last inserted row ID (long number)</param>
        /// <param name="cmdType_">Command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in list of key/value pair format</param>
        /// <param name="transaction_">When not null, transaction to use</param>
        /// <returns>Number of affected rows</returns>
        public int ExecuteNonQuery(string cmdText_, List<KeyValuePair<string, object>> cmdParams_, out long lastInsertedRowId_, CommandType cmdType_ = CommandType.Text, DbTransaction transaction_ = null)
        {
            DbConnection dbConnection = null;
            try
            {
                // Utiliser la connexion de la transaction ou une nouvelle connexion
                dbConnection = transaction_ != null ? transaction_.Connection : CreateConnection();

                // pour l'écriture générique, on caste Parameter[] en IEnumerable (on doit passer un type C#)
                if (!dbConnection.IsBackup)
                {
                    return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, cmdParams_, out lastInsertedRowId_);
                }
                else
                {
                    lock (BackupConnectionUsageLockObject)
                    {
                        return ExecuteNonQuery(dbConnection, transaction_, cmdType_, cmdText_, cmdParams_, out lastInsertedRowId_);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(TraceEventType.Critical, ex.ToString());
                throw;
            }
            finally
            {
                if (transaction_ == null && dbConnection != null)
                {
                    // La connexion n'etait pas celle de la transaction
                    dbConnection.Close();
                    dbConnection.Dispose();
                }

            }
        }

        #endregion

        /// <summary>
        /// Exécution de ExecuteNonQuery() puis ExecuteScalar() pour exécuter une requête de type INSERT et obtenir
        /// l'ID de la ligne insérée.
        /// Utilisation des génériques pour factoriser les 3 représentations en types C# des paramètres ADO.NET.
        /// </summary>
        /// <param name="connection_"></param>
        /// <param name="transaction_"></param>
        /// <param name="cmdParams_"></param>
        /// <param name="lastInsertedRowId_"></param>
        /// <param name="cmdType_"></param>
        /// <param name="cmdText_"></param>
        /// <returns></returns>
        private int ExecuteNonQuery(DbConnection connection_, DbTransaction transaction_, CommandType cmdType_, string cmdText_, IEnumerable cmdParams_, out long lastInsertedRowId_)
         {
            int iNbAffectedRows;
            using (DbCommand command = PrepareCommand(connection_, transaction_, cmdText_, cmdParams_, cmdType_))
            {
                iNbAffectedRows = command.ExecuteNonQuery();
            }

            using (DbCommand command = PrepareCommand(connection_, transaction_, SelectLastInsertIdCommandText))
            {
                object oValue = command.ExecuteScalar();
                if (!Int64.TryParse(oValue.ToString(), out lastInsertedRowId_))
                    throw new Exception("Returned last insert ID value '" + oValue + "' could not be parsed to Long number");
            }
            return iNbAffectedRows;
        }
        #endregion

        #region READER METHODS

        #region PARAMETERLESS METHODS

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <returns>ADO .NET data reader</returns>
        public DbDataReader ExecuteReader(string cmdText_, CommandType cmdType_ = CommandType.Text)
        {
            // Ne pas mettre dans un using la connexion sinon elle sera dipos�e avant d'avoir lu le data reader
            DbConnection dbConnection = CreateConnection();
            using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdType_))
                try
                {
                    DbDataReader dr = command.ExecuteReader(CommandBehavior.CloseConnection);
                    return dr;
                }
                catch (Exception ex)
                {
                    Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_);
                    throw;
                }
        }

        #endregion

        #region OBJECT BASED PARAMETER ARRAY

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in multiple array format</param>
        /// <returns>ADO .NET data reader</returns>
        public DbDataReader ExecuteReader(string cmdText_, object[,] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            // Ne pas mettre dans un using la connexion sinon elle sera dipos�e avant d'avoir lu le data reader
            DbConnection dbConnection = CreateConnection();
            using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                try
                {
                    DbDataReader dr = command.ExecuteReader(CommandBehavior.CloseConnection);
                    return dr;
                }
                catch (Exception ex)
                {
                    Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                    throw;
                }
        }

        #endregion

        #region STRUCTURE BASED PARAMETER ARRAY

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in array of Parameter objects format</param>
        /// <returns>ADO .NET data reader</returns>
        public DbDataReader ExecuteReader(string cmdText_, Parameter[] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            // Ne pas mettre dans un using la connexion sinon elle sera dipos�e avant d'avoir lu le data reader
            DbConnection dbConnection = CreateConnection();
            using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
            {
                try
                {
                    return command.ExecuteReader(CommandBehavior.CloseConnection);
                }
                catch (Exception ex)
                {
                    Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                    throw;
                }
            }
        }

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) formatted as a list of key/value</param>
        /// <returns>ADO .NET data reader</returns>
        public DbDataReader ExecuteReader(string cmdText_, List<KeyValuePair<string, object>> cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            // Ne pas mettre dans un using la connexion sinon elle sera dipos�e avant d'avoir lu le data reader
            DbConnection dbConnection = CreateConnection();
            using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                try
                {
                    return command.ExecuteReader(CommandBehavior.CloseConnection);
                }
                catch (Exception ex)
                {
                    Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                    throw;
                }
        }

        #endregion

        #endregion

        #region ADAPTER METHODS

        #region PARAMETERLESS METHODS

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <returns>ADO .NET dataset</returns>
        public DataSet DataAdapter(string cmdText_, CommandType cmdType_ = CommandType.Text)
        {

            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdType_))
                    try
                    {

                        DbDataAdapter dda = DbProviderFactory.CreateDataAdapter();

                        if (dda == null)
                        {
                            throw new Exception("DbHelper, DataAdapter: data adapter could not be created");
                        }

                        dda.SelectCommand = command.AdoDbCommand;
                        DataSet ds = new DataSet();
                        dda.Fill(ds);
                        return ds;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_);
                        throw;
                    }

            }
        }

        #endregion

        #region OBJECT BASED PARAMETER ARRAY

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in multiple object array format</param>
        /// <returns>ADO .NET dataset</returns>
        public DataSet DataAdapter(string cmdText_, object[,] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {

            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                    try
                    {

                        DbDataAdapter dda = DbProviderFactory.CreateDataAdapter();

                        if (dda == null)
                        {
                            throw new Exception("DbHelper, DataAdapter: data adapter could not be created");
                        }
                        dda.SelectCommand = command.AdoDbCommand;
                        DataSet ds = new DataSet();
                        dda.Fill(ds);
                        return ds;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                        throw;
                    }
            }
        }

        #endregion

        #region STRUCTURE BASED PARAMETER ARRAY

        /// <summary>
        /// Executes a SQL select operation
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in array of Parameter objects format</param>
        /// <returns>ADO .NET dataset</returns>
        public DataSet DataAdapter(string cmdText_, Parameter[] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {

            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                    try
                    {

                        DbDataAdapter dda = DbProviderFactory.CreateDataAdapter();

                        if (dda == null)
                        {
                            throw new Exception("DbHelper, DataAdapter: data adapter could not be created");
                        }
                        dda.SelectCommand = command.AdoDbCommand;
                        DataSet ds = new DataSet();
                        dda.Fill(ds);
                        return ds;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                        throw;
                    }
            }
        }

        #endregion

        #endregion

        #region SCALAR METHODS

        #region PARAMETERLESS METHODS

        /// <summary>
        /// Executes a SQL operation and returns value of first column and first line of data table result.
        /// Generally used for a query such as "count()".
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <returns>data value</returns>
        public object ExecuteScalar(string cmdText_, CommandType cmdType_ = CommandType.Text)
        {
            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdType_))
                    try
                    {
                        return command.ExecuteScalar();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_);
                        throw;
                    }
            }
        }

        #endregion

        #region OBJECT BASED PARAMETER ARRAY

        /// <summary>
        /// Executes a SQL operation and returns value of first column and first line of data table result.
        /// Generally used for a query such as "count()".
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in multiple object array format</param>
        /// <returns>data value</returns>
        public object ExecuteScalar(string cmdText_, object[,] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                    try
                    {
                        return command.ExecuteScalar();

                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                        throw;
                    }
            }
        }

        /// <summary>
        /// Executes a SQL operation and returns value of first column and first line of data table result.
        /// Generally used for a query such as "count()".
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in multiple object array format</param>
        /// <param name="blTransaction_">When true, query will be executed using current transaction. OpenTransaction() should have been called first</param>
        /// <returns>data value</returns>
        public object ExecuteScalar(bool blTransaction_, string cmdText_, object[,] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                    try
                    {
                        return command.ExecuteScalar();

                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                        throw;
                    }
            }
        }


        #endregion

        #region STRUCTURE BASED PARAMETER ARRAY

        /// <summary>
        /// Executes a SQL operation and returns value of first column and first line of data table result.
        /// Generally used for a query such as "count()".
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in array of Parameter objects format</param>
        /// <returns>data value</returns>
        public object ExecuteScalar(string cmdText_, Parameter[] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                    try
                    {
                        return command.ExecuteScalar();

                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                        throw;
                    }
            }
        }

        /// <summary>
        /// Executes a SQL operation and returns value of first column and first line of data table result.
        /// Generally used for a query such as "count()".
        /// </summary>
        /// <param name="cmdType_">SQL command type (Text, StoredProcedure, TableDirect)</param>
        /// <param name="cmdText_">SQL command text</param>
        /// <param name="cmdParams_">ADO.NET parameters (name and value) in array of Parameter objects format</param>
        /// <param name="blTransaction_">When true, query will be executed using current transaction. OpenTransaction() should have been called first</param>
        /// <returns>data value</returns>
        public object ExecuteScalar(bool blTransaction_, string cmdText_, Parameter[] cmdParams_, CommandType cmdType_ = CommandType.Text)
        {
            using (DbConnection dbConnection = CreateConnection())
            {
                using (DbCommand command = PrepareCommand(dbConnection, null, cmdText_, cmdParams_, cmdType_))
                    try
                    {
                        return command.ExecuteScalar();

                    }
                    catch (Exception ex)
                    {
                        Logger.Log(TraceEventType.Critical, ex + " Command was: " + cmdText_ + ", params count: " + command.Parameters.Count);
                        throw;
                    }
            }
        }

        #endregion

        #endregion

    }
}


