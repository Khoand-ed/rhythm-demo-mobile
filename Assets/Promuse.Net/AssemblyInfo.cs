// 让测试走真正的解析路径 / Lets the EditMode tests parse through PromuseApi's own JSON settings
// rather than a copy of them - a test of the wire format is only worth anything if it reads the
// wire the way the game does.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Promuse.Net.EditModeTests")]
