﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ArctosGameServer.Controller;
using ArctosGameServer.Service;

namespace ArctosGameServer
{
    /// <summary>
    /// Interaction logic for Server.xaml
    /// </summary>
    public partial class Server : Window
    {
        public Server()
        {
            InitializeComponent();

            LogBox.TextChanged += (sender, args) =>
            {
                LogBox.CaretIndex = LogBox.Text.Length;
                LogBox.ScrollToEnd();
            }; 
        }
    }
}
