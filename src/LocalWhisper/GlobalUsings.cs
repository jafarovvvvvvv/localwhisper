// Проект тянет и WPF, и WinForms: окна нарисованы на WPF, а иконки в трее в
// WPF нет вовсе. Автоматический набор using-ов из <ImplicitUsings> для такого
// проекта затаскивает System.Windows.Forms глобально — и почти каждое базовое
// имя (Application, Size, Point, TextBox, DataFormats) становится
// двусмысленным в файлах, которым WinForms не нужен вообще.
//
// Поэтому автоматика выключена, а набор задан здесь вручную и по-WPF-ному.
// WinForms остаётся там, где ему и место: в одном файле трея, который
// подключает его сам.

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
