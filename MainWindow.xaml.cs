using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;

namespace BlackMagicHyperDeckControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Receiving byte array  
        byte[] _bytes = new byte[1024];
        System.Text.ASCIIEncoding _asciiEncoder = new System.Text.ASCIIEncoding();
        List<string> _servers = new List<string>( new string[] { "10.100.25.16", "10.100.25.12", "10.100.25.13" } );
        Dictionary<string,Socket> _connections = new Dictionary<string,Socket>();
        bool _isConnected = false;
        static readonly string _path = System.IO.Path.GetDirectoryName( System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName );
        static readonly string _settingsFile = System.IO.Path.Combine( _path + "settings.xml" );

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += this.OnClosing;
            stopButton.IsEnabled = false;
            LoadSettings();
        }

        private void BindServersList()
        {
            serverlist.Items.Clear();
            foreach ( string ip in _servers )
            {
                serverlist.Items.Add( new Server( ip ) );
            }
        }

        private void OnClosing( object sender, System.ComponentModel.CancelEventArgs e )
        {
            this.Disconnect();
        }

        #region Click Handlers
        private void RecordButton_Click( object sender, RoutedEventArgs e )
        {
            if ( ! _isConnected )
            {
                Connect();
            }

            Send( "record" );

            stopButton.IsEnabled = true;
            stopButton.Foreground = Brushes.WhiteSmoke;
            recordButton.Content = "Recording...";
        }

        private void StopButton_Click( object sender, RoutedEventArgs e )
        {
            if ( ! _isConnected )
            {
                Connect();
            }

            Send( "stop" );

            stopButton.IsEnabled = false;
            stopButton.Foreground = Brushes.Gray;
            recordButton.Content = "Record";
        }

        private void PingServers_Click( object sender, RoutedEventArgs e )
        {
            if ( !_isConnected )
            {
                Connect();
            }

            Send( "ping" );

            Keyboard.ClearFocus();
        }

        private void SettingsButton_Click( object sender, RoutedEventArgs e )
        {
            settingsGrid.Visibility = System.Windows.Visibility.Visible;
            appGrid.Visibility = System.Windows.Visibility.Hidden;
            BindServersList();
        }

        private void SaveSettings_Click( object sender, RoutedEventArgs e )
        {
            try
            {
                SaveSettings();
                Output( "settings saved." );
            }
            catch( Exception ex )
            {
                Output( ex.Message );
            }
            settingsGrid.Visibility = System.Windows.Visibility.Hidden;
            appGrid.Visibility = System.Windows.Visibility.Visible;
        }

        private void addServer_Click( object sender, RoutedEventArgs e )
        {
            string ipAddress = inputBox.Text;
            if ( ! _servers.Contains( ipAddress ) )
            {
                _servers.Add( ipAddress );
                BindServersList();
            }

            inputBox.Text = string.Empty;
            Keyboard.ClearFocus();
        }

        private void deleteServerButton_Click( object sender, RoutedEventArgs e )
        {
            Server server = (Server) ( (Button)sender ).DataContext;
            
            if ( _servers.Contains( server.IpAddress ) )
            {
                _servers.Remove( server.IpAddress );
                BindServersList();
            }
        }

        #endregion

        #region Settings Form
        private void OnInputBoxGotFocus( object sender, RoutedEventArgs e )
        {
            inputBox.Foreground = Brushes.Gray;
            if ( inputBox.Text.Equals( "Type a HyperDeck ip address...", StringComparison.OrdinalIgnoreCase ) )
            {
                inputBox.Foreground = Brushes.Black;
                inputBox.Text = string.Empty;
            }
        }

        private void OnInputBoxLostFocus( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrEmpty( inputBox.Text ) )
            {
                inputBox.Foreground = Brushes.Gray;
                inputBox.Text = "Type a HyperDeck ip address...";
            }
        }


        /// <summary>
        /// Loads the previously saved settings.
        /// </summary>
        private void LoadSettings( )
        {
            XmlDocument loadDoc = new XmlDocument();
            try
            {
                loadDoc.Load( _settingsFile );

                // Now get the saved server IPs:
                XmlNode files = loadDoc.SelectSingleNode( "/bmhdcontrol/settings/servers" );
                _servers.Clear();
                foreach ( XmlNode fileItem in files.ChildNodes )
                {
                    _servers.Add( fileItem.Attributes["value"].InnerText );
                }
            }
            catch
            {
                Output( "unable to load settings." );
            }

            BindServersList();
        }

        /// <summary>
        /// Saves the users settings.
        /// </summary>
        private void SaveSettings()
        {
            string path = System.IO.Path.GetDirectoryName( System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName );
            string settingsFile = System.IO.Path.Combine( path + "settings.xml" );
            XmlTextWriter writer = new XmlTextWriter( settingsFile, null );
            writer.WriteStartElement( "bmhdcontrol" );
            writer.WriteString( "\r\n" );
            writer.WriteStartElement( "settings" );
            writer.WriteString( "\r\n\t" );

            // store the saved files
            writer.WriteStartElement( "servers" );
            foreach ( var item in _servers )
            {
                writer.WriteString( "\r\n\t\t" );
                WriteSetting( writer, "server", item.ToString() );
            }
            writer.WriteString( "\r\n\t" );
            writer.WriteEndElement();
            writer.WriteString( "\r\n" );
            writer.WriteEndElement();
            writer.WriteString( "\r\n" );
            writer.WriteEndElement();
            writer.Close();
        }

        private void WriteSetting( XmlTextWriter writer, string name, string value )
        {
            writer.WriteStartElement( name );
            writer.WriteAttributeString( "value", value );
            writer.WriteEndElement();
        }
        #endregion

        /// <summary>
        /// Connect to all servers if not connected.
        /// </summary>
        private void Connect()
        {
            bool allGood = true;
            foreach (var ip in _servers )
            {
                Socket s = null;
                if ( _connections.ContainsKey(ip) )
                {
                    s = _connections[ip];
                }

                if ( s == null || ! s.Connected )
                {
                    s = ConnectServer( ip );
                    if ( s != null )
                    {
                        _connections[ip] = s;
                    }
                    else
                    {
                        allGood = false;
                    }
                }
            }

            if ( allGood )
            {
                Send( "remote: enable: true", echoOutput: false );
                Output( "remote enabled command sent." );
                _isConnected = true;
            }
            else
            {
                Output( "unable to connect to one or more servers." );
            }
        }

        /// <summary>
        /// Connect to the given IP (port 9993) and return the socket.
        /// </summary>
        /// <param name="ip"></param>
        /// <returns>a socket connection to the given IP</returns>
        private Socket ConnectServer(string ip)
        {
            Socket socket = null;
            try
            {
                SocketPermission permission = new SocketPermission(
                    NetworkAccess.Connect,
                    TransportType.Tcp,
                    "",
                    SocketPermission.AllPorts
                    );

                // Ensures permission to access the socket 
                permission.Demand();

                IPAddress ipAddr = IPAddress.Parse( ip );
                //IPEndPoint ipEndPoint = new IPEndPoint( ipAddr, 9993 );

                // Create one Socket object to setup Tcp connection 
                socket = new Socket( ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp );
                socket.NoDelay = false;

                // Establishes a connection to a remote host, timeout in 1 second (plenty of time in this day & age).
                IAsyncResult result = socket.BeginConnect( ipAddr, 9993, null, null );
                bool success = result.AsyncWaitHandle.WaitOne( 1000, true );
                if ( !success )
                {
                    // CLOSE THE SOCKET!
                    socket.Close();
                    Output( string.Format( "unable to connect to {0}", ip ) );
                    return null;
                }

                // connect to a remote host 
                //socket.Connect( ipEndPoint );

                Output( string.Format( "connected to {0}", socket.RemoteEndPoint ) );

                ReceiveDataFromServer( socket );
            }
            catch ( Exception exc ) { MessageBox.Show( exc.ToString() ); }

            return socket;
        }

        private void Disconnect()
        {
            Send( "remote: enable: false", echoOutput: false );

            foreach( Socket senderSock in _connections.Values )
            {
                try
                {
                    // Disables sends and receives on a Socket. 
                    senderSock.Shutdown( SocketShutdown.Both );

                    //Closes the Socket connection and releases all resources 
                    senderSock.Close();
                    Output( "disconnected" );
                }
                catch ( Exception exc ) { MessageBox.Show( exc.ToString() ); }
            }
        } 

        private void Output( string message, bool newline = true)
        {
            string crlf = string.Empty;
            string appendCrlf = string.Empty;

            if ( newline )
            {
                crlf = System.Environment.NewLine;
            }

            output.AppendText( string.Format( "{0}{1}", message, crlf ) );
            output.ScrollToEnd();
        }

        private void Send( string message, bool echoOutput = true )
        {
            try
            {
                byte[] msg = _asciiEncoder.GetBytes( message + System.Environment.NewLine );

                // Sends data to a connected Socket.
                foreach ( Socket senderSock in _connections.Values )
                {
                    int bytesSend = senderSock.Send( msg );
                    ReceiveDataFromServer( senderSock );
                }

                if ( _isConnected && echoOutput )
                {
                    Output( string.Format( "{0} command sent.", message ) );
                }
            }
            catch ( Exception exc ) { MessageBox.Show( exc.ToString() ); }
        }

        private void ReceiveDataFromServer(Socket senderSock)
        {
            try
            {
                // get data from the socket
                int bytesRec = senderSock.Receive( _bytes );

                // convert the byte array to a string 
                String theMessageToReceive = System.Text.Encoding.ASCII.GetString( _bytes, 0, bytesRec );

                // read the data untill there is no more
                while ( senderSock.Available > 0 )
                {
                    bytesRec = senderSock.Receive( _bytes );
                    theMessageToReceive += System.Text.Encoding.ASCII.GetString( _bytes, 0, bytesRec );
                }

                Output( theMessageToReceive, newline: false );
            }
            catch ( Exception exc ) { MessageBox.Show( exc.ToString() ); }
        }

        protected class Server
        {
            public Server( string ip )
            {
                IpAddress = ip;
            }

            public string IpAddress { get; set; }
        }

    }
}
