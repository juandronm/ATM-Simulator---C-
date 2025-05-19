using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.OleDb;
using System.IO;


namespace ATMSimulator_1
{
    public partial class CultureBank : Form 
    {
        OleDbConnection conn;
        private string currentAccount = "";
        private decimal currentAccountDailyLimit = 0; // To store fetched daily limit


        // --- UI State Flags ---
        private bool isAuthenticated = false;
        private bool isWaitingForPassword = false;
        private bool isWaitingToAddNewUser = false;


        private enum AtmMode { None, Deposit, Withdraw, TransferAmount, TransferReceiver, History }
        private AtmMode currentMode = AtmMode.None;


        private TextBox currentInputBox = null;


        public CultureBank()
        {
            InitializeComponent();
            InitializeDatabaseConnection();
            SetupInputHandlers();
            ResetToInitialState(); // Start clean
        }


        private void InitializeDatabaseConnection()
        {
            // Navigate three levels up from Application.StartupPath to find the project root, then go into the DB folder
            string projectRoot = Directory.GetParent(Application.StartupPath)?.Parent?.Parent?.FullName;
            if (projectRoot == null)
            {
                MessageBox.Show("Error: Could not determine the project root directory. Place 'bankaccounts.accdb' in a 'Database' subfolder of your project.", "Database Path Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            string dbPath = Path.Combine(projectRoot, "Database", "bankaccounts.accdb");


            if (!File.Exists(dbPath))
            {
                MessageBox.Show($"Error: Database file not found at '{dbPath}'.\nPlease ensure 'bankaccounts.accdb' exists in a 'Database' subfolder of your project.", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            string connectionString = $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={dbPath}";
            conn = new OleDbConnection(connectionString);


            try
            {
                conn.Open();
                // MessageBox.Show("✅ Connection successful!"); 
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Connection failed: " + ex.Message, "Database Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void SetupInputHandlers()
        {
            textBox1.KeyPress += SuppressTyping; // Account Number input
            textBoxUsername.KeyPress += SuppressTyping; // New user account
            textBoxPassword.KeyPress += SuppressTyping; // Password input
            textBoxTransferAmount.KeyPress += SuppressTyping;
            textBoxReceiverAccount.KeyPress += SuppressTyping;
            textBoxDeposit.KeyPress += SuppressTyping;
            textBoxWithdrawAmount.KeyPress += SuppressTyping; 


            textBox1.KeyDown += AllowBackspace;
            textBoxUsername.KeyDown += AllowBackspace;
            textBoxPassword.KeyDown += AllowBackspace;
            textBoxTransferAmount.KeyDown += AllowBackspace;
            textBoxReceiverAccount.KeyDown += AllowBackspace;
            textBoxDeposit.KeyDown += AllowBackspace;
            textBoxWithdrawAmount.KeyDown += AllowBackspace; 


            textBox1.Enter += (s, e) => currentInputBox = textBox1;
            textBoxUsername.Enter += (s, e) => currentInputBox = textBoxUsername;
            textBoxPassword.Enter += (s, e) => currentInputBox = textBoxPassword;
            textBoxTransferAmount.Enter += (s, e) => currentInputBox = textBoxTransferAmount;
            textBoxReceiverAccount.Enter += (s, e) => currentInputBox = textBoxReceiverAccount;
            textBoxDeposit.Enter += (s, e) => currentInputBox = textBoxDeposit;
            textBoxWithdrawAmount.Enter += (s, e) => currentInputBox = textBoxWithdrawAmount; 


            this.KeyPreview = true; 
            this.KeyDown += Form1_KeyDown;
        }


        private void AllowBackspace(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back && currentInputBox != null && currentInputBox.Text.Length > 0)
            {
                currentInputBox.Text = currentInputBox.Text.Substring(0, currentInputBox.Text.Length - 1);
                currentInputBox.SelectionStart = currentInputBox.Text.Length;
                e.SuppressKeyPress = true; 
            }
        }
        private void SuppressTyping(object sender, KeyPressEventArgs e)
        {
            // Allow backspace if not handled by KeyDown (though AllowBackspace should handle it)
            if (e.KeyChar == (char)Keys.Back)
            {
                e.Handled = false;
                return;
            }
            e.Handled = true; 
        }


        private void NumberButton_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null || currentInputBox == null) return;


            currentInputBox.Text += btn.Text;
            currentInputBox.SelectionStart = currentInputBox.Text.Length; 
        }


        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            // This allows physical numpad/number keys to work
            Button targetButton = null;
            switch (e.KeyCode)
            {
                case Keys.D0: case Keys.NumPad0: targetButton = button0; break;
                case Keys.D1: case Keys.NumPad1: targetButton = button1; break;
                case Keys.D2: case Keys.NumPad2: targetButton = button2; break;
                case Keys.D3: case Keys.NumPad3: targetButton = button3; break;
                case Keys.D4: case Keys.NumPad4: targetButton = button4; break;
                case Keys.D5: case Keys.NumPad5: targetButton = button5; break;
                case Keys.D6: case Keys.NumPad6: targetButton = button6; break;
                case Keys.D7: case Keys.NumPad7: targetButton = button7; break;
                case Keys.D8: case Keys.NumPad8: targetButton = button8; break;
                case Keys.D9: case Keys.NumPad9: targetButton = button9; break;
                case Keys.Back: buttonBackspace.PerformClick(); break; 
                case Keys.Enter: CheckButton.PerformClick(); break; 
            }
            targetButton?.PerformClick();
            if (targetButton != null || e.KeyCode == Keys.Back || e.KeyCode == Keys.Enter)
            {
                e.Handled = true; 
                e.SuppressKeyPress = true; 
            }
        }


        private void CheckButton_Click(object sender, EventArgs e) // Main "Enter" or "Confirm" button
        {
            if (isWaitingToAddNewUser) HandleAddNewUser();
            else if (!isAuthenticated && !isWaitingForPassword) HandleAccountLookup();
            else if (!isAuthenticated && isWaitingForPassword) HandlePasswordValidation();
            else if (isAuthenticated)
            {
                switch (currentMode)
                {
                    case AtmMode.Deposit: HandleDeposit(); break;
                    case AtmMode.Withdraw: HandleWithdrawal(); break;
                    case AtmMode.TransferAmount: HandleTransferAmountEntry(); break;
                    case AtmMode.TransferReceiver: HandleTransferReceiverEntry(); break;
                    
                }
            }
        }


        private void HandleAccountLookup()
        {
            string inputAccount = textBox1.Text.Trim();
            if (string.IsNullOrEmpty(inputAccount))
            {
                MessageBox.Show("Please enter an account number.");
                return;
            }


            try
            {
                conn.Open();
                string query = "SELECT COUNT(*), [Daily Limit] FROM Clients WHERE [Client No] = ? GROUP BY [Daily Limit]";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", inputAccount);


                using (OleDbDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read() && Convert.ToInt32(reader[0]) > 0)
                    {
                        currentAccount = inputAccount;
                        currentAccountDailyLimit = reader[1] != DBNull.Value ? Convert.ToDecimal(reader[1]) : 0m;
                        isWaitingForPassword = true;
                        UpdateUIAfterAccountLookup();
                    }
                    else
                    {
                        MessageBox.Show("Account not found.");
                        textBox1.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error checking account: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void HandlePasswordValidation()
        {
            string pass = textBoxPassword.Text.Trim();
            if (string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Please enter your password.");
                return;
            }


            try
            {
                conn.Open();
                string query = "SELECT [Password] FROM Clients WHERE [Client No] = ?";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", currentAccount);
                object result = cmd.ExecuteScalar();


                if (result != null && result.ToString() == pass)
                {
                    isAuthenticated = true;
                    isWaitingForPassword = false;
                    MessageBox.Show($"Login successful. Welcome, Account {currentAccount}!");
                    ShowPostLoginOptions();
                }
                else
                {
                    MessageBox.Show("Incorrect password.");
                    textBoxPassword.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Login error: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void HandleAddNewUser()
        {
            string newUser = textBoxUsername.Text.Trim();
            string newPass = textBoxPassword.Text.Trim(); 


            if (string.IsNullOrEmpty(newUser) || string.IsNullOrEmpty(newPass))
            {
                MessageBox.Show("Please enter both account number and password for the new user.");
                return;
            }
            if (newUser.Length > 10) 
            {
                MessageBox.Show("Account number cannot exceed 10 characters.");
                return;
            }
            if (newPass.Length < 4) 
            {
                MessageBox.Show("Password must be at least 4 characters long.");
                return;
            }




            try
            {
                conn.Open();
                string checkQuery = "SELECT COUNT(*) FROM Clients WHERE [Client No] = ?";
                OleDbCommand checkCmd = new OleDbCommand(checkQuery, conn);
                checkCmd.Parameters.AddWithValue("?", newUser);
                if ((int)checkCmd.ExecuteScalar() > 0)
                {
                    MessageBox.Show("Account number already exists.");
                    return;
                }

                string insertQuery = "INSERT INTO Clients ([Client No], Funds, [Daily Limit], [Password]) VALUES (?, ?, ?, ?)";
                OleDbCommand insertCmd = new OleDbCommand(insertQuery, conn);
                insertCmd.Parameters.AddWithValue("?", newUser);
                insertCmd.Parameters.AddWithValue("?", 0m); // Initial funds
                insertCmd.Parameters.AddWithValue("?", 500m); // Default daily limit, adjust as needed
                insertCmd.Parameters.AddWithValue("?", newPass); // Plain text password
                insertCmd.ExecuteNonQuery();


                MessageBox.Show("User successfully registered. You can now log in.");
                LogTransaction(newUser, "Account Created", 0, $"Initial setup with daily limit ${500m}");
                ResetToInitialState();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Registration error: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void HandleDeposit()
        {
            if (!decimal.TryParse(textBoxDeposit.Text.Trim(), out decimal amountToDeposit) || amountToDeposit <= 0)
            {
                MessageBox.Show("Please enter a valid positive deposit amount.");
                return;
            }
            if (amountToDeposit > 10000) 
            {
                MessageBox.Show("Deposit limit per transaction is $10,000.");
                return;
            }


            try
            {
                conn.Open();
                string query = "SELECT Funds FROM Clients WHERE [Client No] = ?";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", currentAccount);
                decimal currentFunds = Convert.ToDecimal(cmd.ExecuteScalar());
                decimal newFunds = currentFunds + amountToDeposit;


                string updateQuery = "UPDATE Clients SET Funds = ? WHERE [Client No] = ?";
                OleDbCommand updateCmd = new OleDbCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("?", newFunds);
                updateCmd.Parameters.AddWithValue("?", currentAccount);
                updateCmd.ExecuteNonQuery();


                LogTransaction(currentAccount, "Deposit", amountToDeposit);
                MessageBox.Show($"Successfully deposited ${amountToDeposit:C2}. New balance: {newFunds:C2}");
                ShowPostLoginOptions(); // Go back to main menu, updates balance display
            }
            catch (Exception ex)
            {
                MessageBox.Show("Deposit error: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void HandleWithdrawal()
        {
            if (!decimal.TryParse(textBoxWithdrawAmount.Text.Trim(), out decimal amountToWithdraw) || amountToWithdraw <= 0)
            {
                MessageBox.Show("Please enter a valid positive withdrawal amount.");
                return;
            }
            if (amountToWithdraw % 10 != 0) 
            {
                MessageBox.Show("Withdrawal amount must be in multiples of $10.");
                return;
            }
            // Simplified: Using DailyLimit as per-transaction withdrawal cap
            if (amountToWithdraw > currentAccountDailyLimit && currentAccountDailyLimit > 0) 
            {
                MessageBox.Show($"Withdrawal amount exceeds your per-transaction limit of ${currentAccountDailyLimit:C2}.");
                return;
            }
           
            try
            {
                conn.Open();
                string query = "SELECT Funds FROM Clients WHERE [Client No] = ?";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", currentAccount);
                decimal currentFunds = Convert.ToDecimal(cmd.ExecuteScalar());


                if (amountToWithdraw > currentFunds)
                {
                    MessageBox.Show("Insufficient funds.");
                    return;
                }


                decimal newFunds = currentFunds - amountToWithdraw;
                string updateQuery = "UPDATE Clients SET Funds = ? WHERE [Client No] = ?";
                OleDbCommand updateCmd = new OleDbCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("?", newFunds);
                updateCmd.Parameters.AddWithValue("?", currentAccount);
                updateCmd.ExecuteNonQuery();


                LogTransaction(currentAccount, "Withdrawal", amountToWithdraw);
                MessageBox.Show($"Successfully withdrew ${amountToWithdraw:C2}. New balance: {newFunds:C2}");
                ShowPostLoginOptions();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Withdrawal error: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void HandleTransferAmountEntry()
        {
            if (!decimal.TryParse(textBoxTransferAmount.Text.Trim(), out decimal amountToTransfer) || amountToTransfer <= 0)
            {
                MessageBox.Show("Please enter a valid positive amount to transfer.");
                return;
            }


            try
            {
                conn.Open();
                string query = "SELECT Funds FROM Clients WHERE [Client No] = ?";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", currentAccount);
                decimal currentFunds = Convert.ToDecimal(cmd.ExecuteScalar());
                conn.Close(); // Close early, will reopen for next step


                if (amountToTransfer > currentFunds)
                {
                    MessageBox.Show("Insufficient funds for this transfer.");
                    return;
                }
                currentMode = AtmMode.TransferReceiver;
                UpdateUIAfterTransferAmountEntered();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error checking funds for transfer: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void HandleTransferReceiverEntry()
        {
            string receiverAccount = textBoxReceiverAccount.Text.Trim();
            if (string.IsNullOrEmpty(receiverAccount))
            {
                MessageBox.Show("Please enter the receiver's account number.");
                return;
            }
            if (receiverAccount == currentAccount)
            {
                MessageBox.Show("Cannot transfer funds to your own account.");
                return;
            }
            if (!decimal.TryParse(textBoxTransferAmount.Text.Trim(), out decimal amountToTransfer))
            {
                MessageBox.Show("Error: Transfer amount is invalid. Please restart transfer."); // Should not happen if previous step was OK
                ShowPostLoginOptions();
                return;
            }


            OleDbTransaction transaction = null;
            try
            {
                conn.Open();
                transaction = conn.BeginTransaction(); // Use a transaction for atomicity

                string receiverQuery = "SELECT Funds FROM Clients WHERE [Client No] = ?";
                OleDbCommand receiverCmd = new OleDbCommand(receiverQuery, conn, transaction);
                receiverCmd.Parameters.AddWithValue("?", receiverAccount);
                object receiverFundsObj = receiverCmd.ExecuteScalar();


                if (receiverFundsObj == null)
                {
                    MessageBox.Show("Receiver account not found.");
                    transaction.Rollback();
                    return;
                }
                decimal receiverFunds = Convert.ToDecimal(receiverFundsObj);


                string senderQuery = "SELECT Funds FROM Clients WHERE [Client No] = ?";
                OleDbCommand senderCmd = new OleDbCommand(senderQuery, conn, transaction);
                senderCmd.Parameters.AddWithValue("?", currentAccount);
                decimal senderFunds = Convert.ToDecimal(senderCmd.ExecuteScalar());


                if (amountToTransfer > senderFunds) // Double check, though done before
                {
                    MessageBox.Show("Insufficient funds.");
                    transaction.Rollback();
                    return;
                }


                decimal newSenderFunds = senderFunds - amountToTransfer;
                decimal newReceiverFunds = receiverFunds + amountToTransfer;


                string updateSenderQuery = "UPDATE Clients SET Funds = ? WHERE [Client No] = ?";
                OleDbCommand updateSenderCmd = new OleDbCommand(updateSenderQuery, conn, transaction);
                updateSenderCmd.Parameters.AddWithValue("?", newSenderFunds);
                updateSenderCmd.Parameters.AddWithValue("?", currentAccount);
                updateSenderCmd.ExecuteNonQuery();


                string updateReceiverQuery = "UPDATE Clients SET Funds = ? WHERE [Client No] = ?";
                OleDbCommand updateReceiverCmd = new OleDbCommand(updateReceiverQuery, conn, transaction);
                updateReceiverCmd.Parameters.AddWithValue("?", newReceiverFunds);
                updateReceiverCmd.Parameters.AddWithValue("?", receiverAccount);
                updateReceiverCmd.ExecuteNonQuery();


                transaction.Commit(); // Commit all changes


                LogTransaction(currentAccount, "Transfer Sent", amountToTransfer, $"To: {receiverAccount}");
                LogTransaction(receiverAccount, "Transfer Received", amountToTransfer, $"From: {currentAccount}");


                MessageBox.Show($"Successfully transferred ${amountToTransfer:C2} to account {receiverAccount}.");
                ShowPostLoginOptions();
            }
            catch (Exception ex)
            {
                transaction?.Rollback(); // Rollback on error
                MessageBox.Show("Transfer error: " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        private void LogTransaction(string clientNo, string type, decimal amount, string notes = null)
        {
            try
            {
                conn.Open();
                string query = "INSERT INTO Transactions (ClientNo, TransactionType, Amount, Notes, TransactionDate) VALUES (?, ?, ?, ?, NOW())";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", clientNo);
                cmd.Parameters.AddWithValue("?", type);
                cmd.Parameters.AddWithValue("?", amount);
                cmd.Parameters.AddWithValue("?", (object)notes ?? DBNull.Value); // Handle null notes
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to log transaction: {ex.Message}");
                // MessageBox.Show("Warning: Could not record transaction to history. " + ex.Message);
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }


        // --- UI Update Methods ---
        private void ResetToInitialState()
        {
            isAuthenticated = false;
            isWaitingForPassword = false;
            isWaitingToAddNewUser = false;
            currentMode = AtmMode.None;
            currentAccount = "";
            currentInputBox = textBox1; // Default input


            // Clear all input fields
            textBox1.Clear(); // Account number
            textBoxUsername.Clear();
            textBoxPassword.Clear();
            textBoxDeposit.Clear();
            textBoxWithdrawAmount.Clear();
            textBoxTransferAmount.Clear();
            textBoxReceiverAccount.Clear();
            listBoxHistory.Items.Clear(); // Clear history display


            // --- Visibility: Initial Login Screen ---
            label1.Text = "Enter Account Number:"; // Assuming label1 is for account number
            label1.Visible = true;
            textBox1.Visible = true;


            labelUsername.Visible = false;
            textBoxUsername.Visible = false;
            labelPassword.Visible = false;
            textBoxPassword.Visible = false;


            // Hide operation-specific UI
            labelDeposit.Visible = false;
            textBoxDeposit.Visible = false;
            labelWithdrawAmount.Visible = false; // Ensure this label exists
            textBoxWithdrawAmount.Visible = false; // Ensure this textbox exists
            howMuchLabel.Visible = false; // Transfer amount label
            textBoxTransferAmount.Visible = false;
            labelReceiverAccount.Visible = false;
            textBoxReceiverAccount.Visible = false;
            listBoxHistory.Visible = false; // Ensure this listbox exists
            labelBalance.Visible = false;


            // --- Button States: Initial Login Screen ---
            CheckButton.Enabled = true; // Main "Enter" button
            addButton.Enabled = true; // "Add New User"
            buttonDeposit.Visible = false;
            buttonWithdraw.Visible = false; // Ensure this button exists
            buttonTransfer.Visible = false;
            buttonHistory.Visible = false; // Ensure this button exists
            buttonLogout.Visible = false; // Ensure this button exists (or manage visibility of cancel)


            textBox1.Focus();
        }


        private void UpdateUIAfterAccountLookup()
        {
            label1.Visible = false;
            textBox1.Visible = false;


            labelPassword.Text = "Enter Password:";
            labelPassword.Visible = true;
            textBoxPassword.Visible = true;
            textBoxPassword.Clear();
            currentInputBox = textBoxPassword;
            textBoxPassword.Focus();


            addButton.Enabled = false; 
        }


        private void ShowPostLoginOptions()
        {
            isAuthenticated = true; 
            currentMode = AtmMode.None; 
            isWaitingForPassword = false;
            isWaitingToAddNewUser = false;
            currentInputBox = null; 


            textBoxPassword.Clear();
            textBoxDeposit.Clear();
            textBoxWithdrawAmount.Clear();
            textBoxTransferAmount.Clear();
            textBoxReceiverAccount.Clear();
            listBoxHistory.Items.Clear();




            // --- Visibility: Post-Login Main Menu ---
            label1.Visible = false; // Hide account number entry
            textBox1.Visible = false;
            labelUsername.Visible = false;
            textBoxUsername.Visible = false;
            labelPassword.Visible = false;
            textBoxPassword.Visible = false;


            // Hide all specific operation UI elements first
            labelDeposit.Visible = false;
            textBoxDeposit.Visible = false;
            labelWithdrawAmount.Visible = false;
            textBoxWithdrawAmount.Visible = false;
            howMuchLabel.Visible = false;
            textBoxTransferAmount.Visible = false;
            labelReceiverAccount.Visible = false;
            textBoxReceiverAccount.Visible = false;
            listBoxHistory.Visible = false;


            // Show Balance
            try
            {
                conn.Open();
                string query = "SELECT Funds FROM Clients WHERE [Client No] = ?";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", currentAccount);
                object result = cmd.ExecuteScalar();
                if (result != null)
                {
                    labelBalance.Text = $"Account: {currentAccount}\nBalance: {Convert.ToDecimal(result):C2}";
                    labelBalance.Visible = true;
                }
            }
            catch (Exception ex) { MessageBox.Show("Failed to load balance: " + ex.Message); }
            finally { if (conn.State == ConnectionState.Open) conn.Close(); }




            // --- Button States: Post-Login Main Menu ---
            CheckButton.Enabled = false; // "Enter" not used directly from main menu
            addButton.Enabled = false; // No adding user when logged in


            buttonDeposit.Visible = true;
            buttonWithdraw.Visible = true;
            buttonTransfer.Visible = true;
            buttonHistory.Visible = true;
            buttonLogout.Visible = true; // Show Logout
        }


        private void ShowDepositUI()
        {
            currentMode = AtmMode.Deposit;
            HideAllActionButtonsAndSpecificUI();
            labelBalance.Visible = true; // Keep balance visible


            labelDeposit.Visible = true;
            textBoxDeposit.Visible = true;
            textBoxDeposit.Clear();
            currentInputBox = textBoxDeposit;
            textBoxDeposit.Focus();


            CheckButton.Enabled = true; 
        }
        private void ShowWithdrawUI()
        {
            currentMode = AtmMode.Withdraw;
            HideAllActionButtonsAndSpecificUI();
            labelBalance.Visible = true; 


            labelWithdrawAmount.Visible = true; 
            textBoxWithdrawAmount.Visible = true;
            textBoxWithdrawAmount.Clear();
            currentInputBox = textBoxWithdrawAmount;
            textBoxWithdrawAmount.Focus();


            CheckButton.Enabled = true;
        }


        private void ShowTransferAmountUI()
        {
            currentMode = AtmMode.TransferAmount;
            HideAllActionButtonsAndSpecificUI();
            labelBalance.Visible = true; 


            howMuchLabel.Text = "Enter Amount to Transfer:"; 
            howMuchLabel.Visible = true;
            textBoxTransferAmount.Visible = true;
            textBoxTransferAmount.Clear();
            currentInputBox = textBoxTransferAmount;
            textBoxTransferAmount.Focus();


            CheckButton.Enabled = true;
        }


        private void UpdateUIAfterTransferAmountEntered()
        {
            HideAllActionButtonsAndSpecificUI();
            labelBalance.Visible = true; // Keep balance visible


            howMuchLabel.Text = $"Amount: {textBoxTransferAmount.Text}"; 
            howMuchLabel.Visible = true;
            textBoxTransferAmount.Visible = false; 

            labelReceiverAccount.Visible = true;
            textBoxReceiverAccount.Visible = true;
            textBoxReceiverAccount.Clear();
            currentInputBox = textBoxReceiverAccount;
            textBoxReceiverAccount.Focus();


            CheckButton.Enabled = true;
        }




        private void ShowHistoryUI()
        {
            currentMode = AtmMode.History;
            HideAllActionButtonsAndSpecificUI();
            labelBalance.Visible = true; 


            listBoxHistory.Visible = true; 
            listBoxHistory.Items.Clear();
            CheckButton.Enabled = false; 


            try
            {
                conn.Open();
                string query = "SELECT TOP 15 TransactionDate, TransactionType, Amount, Notes FROM Transactions WHERE ClientNo = ? ORDER BY TransactionDate DESC";
                OleDbCommand cmd = new OleDbCommand(query, conn);
                cmd.Parameters.AddWithValue("?", currentAccount);
                using (OleDbDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.HasRows)
                    {
                        listBoxHistory.Items.Add("No transactions found.");
                    }
                    else
                    {
                        listBoxHistory.Items.Add($"Transaction History for Account: {currentAccount}");
                        listBoxHistory.Items.Add("----------------------------------------------------");
                        while (reader.Read())
                        {
                            DateTime date = reader.GetDateTime(0);
                            string type = reader.GetString(1);
                            decimal amount = reader.GetDecimal(2);
                            string notes = reader.IsDBNull(3) ? "" : reader.GetString(3);
                            listBoxHistory.Items.Add($"{date:yyyy-MM-dd HH:mm} | {type,-18} | {amount,10:C2} | {notes}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error fetching history: " + ex.Message);
                listBoxHistory.Items.Add("Error loading history.");
            }
            finally
            {
                if (conn.State == ConnectionState.Open) conn.Close();
            }
        }
        private void ShowAddNewUserUI()
        {
            isWaitingToAddNewUser = true;
            isAuthenticated = false;
            isWaitingForPassword = false;
            currentMode = AtmMode.None;
            currentInputBox = textBoxUsername; 


            // Clear relevant fields
            textBox1.Clear();
            textBoxUsername.Clear();
            textBoxPassword.Clear(); 


            // --- Visibility: Add New User Screen ---
            label1.Visible = false; // Hide initial account entry
            textBox1.Visible = false;


            labelUsername.Text = "Enter New Account No:";
            labelUsername.Visible = true;
            textBoxUsername.Visible = true;


            labelPassword.Text = "Enter New Password:"; // Reuse password label/textbox
            labelPassword.Visible = true;
            textBoxPassword.Visible = true;




            // Hide operation-specific UI
            labelDeposit.Visible = false; textBoxDeposit.Visible = false;
            labelWithdrawAmount.Visible = false; textBoxWithdrawAmount.Visible = false;
            howMuchLabel.Visible = false; textBoxTransferAmount.Visible = false;
            labelReceiverAccount.Visible = false; textBoxReceiverAccount.Visible = false;
            listBoxHistory.Visible = false;
            labelBalance.Visible = false;


            // --- Button States: Add New User Screen ---
            CheckButton.Enabled = true; 
            addButton.Enabled = false; 
            buttonDeposit.Visible = false;
            buttonWithdraw.Visible = false;
            buttonTransfer.Visible = false;
            buttonHistory.Visible = false;
            buttonLogout.Visible = true; 

            textBoxUsername.Focus();
        }


        private void HideAllActionButtonsAndSpecificUI()
        {
            // Hide main action buttons
            buttonDeposit.Visible = false;
            buttonWithdraw.Visible = false;
            buttonTransfer.Visible = false;
            buttonHistory.Visible = false;
            buttonLogout.Visible = true; 


            // Hide all specific input areas
            labelDeposit.Visible = false; textBoxDeposit.Visible = false;
            labelWithdrawAmount.Visible = false; textBoxWithdrawAmount.Visible = false;
            howMuchLabel.Visible = false; textBoxTransferAmount.Visible = false;
            labelReceiverAccount.Visible = false; textBoxReceiverAccount.Visible = false;
            listBoxHistory.Visible = false;
            

            CheckButton.Enabled = false; 
        }


        private void buttonBackspace_Click(object sender, EventArgs e)
        {
            if (currentInputBox != null && currentInputBox.Text.Length > 0)
            {
                currentInputBox.Text = currentInputBox.Text.Substring(0, currentInputBox.Text.Length - 1);
                currentInputBox.SelectionStart = currentInputBox.Text.Length;
            }
        }


        private void cancelButton_Click(object sender, EventArgs e) 
        {
            if (isAuthenticated)
            {
                if (currentMode != AtmMode.None)
                {
                    ShowPostLoginOptions(); 
                }
                else 
                {
                    HandleLogout();
                }
            }
            else if (isWaitingToAddNewUser || isWaitingForPassword)
            {
                ResetToInitialState(); 
            }
            else 
            {
                textBox1.Clear(); 
                currentInputBox = textBox1;
                textBox1.Focus();
            }
        }


        private void addAccount_Click(object sender, EventArgs e) 
        {
            ShowAddNewUserUI();
        }


        private void buttonDeposit_Click(object sender, EventArgs e)
        {
            ShowDepositUI();
        }


        private void buttonWithdraw_Click(object sender, EventArgs e) 
        {
            ShowWithdrawUI();
        }


        private void buttonTransfer_Click(object sender, EventArgs e)
        {
            ShowTransferAmountUI();
        }


        private void buttonHistory_Click(object sender, EventArgs e) 
        {
            ShowHistoryUI();
        }
        private void buttonLogout_Click(object sender, EventArgs e) 
        {
            HandleLogout();
        }
        private void HandleLogout()
        {
            if (isAuthenticated)
            {
                MessageBox.Show($"Account {currentAccount} logged out. Thank you!");
            }
            ResetToInitialState();
        }
    }
}
