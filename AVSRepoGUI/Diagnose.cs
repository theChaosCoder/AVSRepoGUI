using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AVSRepoGUI
{
    public class Diagnose
    {
        public string python_bin = "python.exe";
        

        public Diagnose(string pythonbin)
        {
            this.python_bin = pythonbin;
        }

        public async Task<string> GetVapoursynthVersion()
        {
            return await Task.Run(() => run_python("import vapoursynth as vs; core = vs.get_core(); print(core.version())"));
        }


        public async Task<string> GetPythonLocation()
        {
            return await Task.Run(() => run_python("import sys; print(sys.executable)").Trim());
        }
        
        public async Task<Depends> GetDllDependencies(string file)
        {
            var result = await Task.Run(() => run_listpedeps(file));
            var dep = ParseDepends(result);
            if (dep == null)
                return null;
            else
                return dep;
        }


        private Depends ParseDepends(string input)
        {
            var dep = new Depends();

            var lines = input.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
  
            dep.file = input;
            dep.architecture = Array.FindAll(lines, c => c.Contains("architecture:"))[0].Split(':')[1].Trim();
            dep.machine_name = Array.FindAll(lines, c => c.Contains("machine name:"))[0].Split(':')[1].Trim();
            dep.subsystem = Array.FindAll(lines, c => c.Contains("subsystem:"))[0].Split(':')[1].Trim();
            dep.minimum_windows_version = Array.FindAll(lines, c => c.Contains("minimum Windows version:"))[0].Split(':')[1].Trim();

            var split_import = input.Split(new string[] { "IMPORTS" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "EXPORTS" }, StringSplitOptions.RemoveEmptyEntries)[0].Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach(var import in split_import)
            {
                var imp_tmp = import.Split(':');
                if(imp_tmp[0].Trim() != "KERNEL32.dll")
                {
                    dep.Imports.Add(imp_tmp[0].Trim());
                }
            }
            dep.Imports = dep.Imports.Distinct().ToList();

            var split_export = input.Split(new string[] { "EXPORTS" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var export in split_export)
            {
                var exp_tmp = export.Split(':');
                dep.Exports.Add(exp_tmp[0].Trim());
                if (exp_tmp[1].Trim().Contains("VapourSynthPluginInit"))
                    dep.IsVapourSynthPlugin = true;
                if (exp_tmp[1].Trim().Contains("AvisynthPluginInit"))
                    dep.IsAvisynthPlugin = true;
            }
            dep.Exports = dep.Exports.Distinct().ToList();

            return dep;
        }


        public Dictionary<string, List<string>> CheckDuplicateAvsScripts(string path) //Dictionary<string, List<string>>
        {
            var script_functions = new Dictionary<string, string>();
            var script_functions_dups = new Dictionary<string, List<string>>();

            //string pattern = @"function\s+([a-zA-Z_{1}][a-zA-Z0-9_]+).+[\s|\S](?=\()";
            //string pattern = @"function\s+.+[\s|\S](?=\()";
            //string pattern = @"^[f-fF-F]unction\s\w+";
            //string pattern = @"^(?!#|assert|.+#.+function)(.+|)function\s+\w+"; // good
            //string pattern = @"^(?!#|assert|.+#.+function)(.+|)function\s+\w+(\s+)?(?=\()"; // misses stuff in srestore.avsi and others
            string pattern = @"^(?!#|assert|.+#.+function)(.+|)function\s+\w+(\s+|.{5})?(?=\()"; // best?
            string[] filePaths = Directory.GetFiles(path, "*.avsi");

            foreach (var file in filePaths)
            {
                Console.WriteLine(file);
                foreach (Match m in Regex.Matches(File.ReadAllText(file, Encoding.UTF8), pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                {
                    var potential_function = m.Value.Trim();
                    Console.WriteLine("'{0}' found at index {1}.", potential_function, m.Index);
                    if (potential_function.Split(' ').Length == 2) // check for valid function (in case the regex finds an invalid string)
                    {
                        string script_func = potential_function.Split(' ')[1].Trim();
                        //Console.WriteLine("'{0}' found at index {1}.", m.Value.Trim(), m.Index);
                        if (script_functions.ContainsKey(script_func))
                        {
                            if (!script_functions_dups.ContainsKey(script_func))
                            {
                                script_functions_dups[script_func] = new List<string>() { script_functions[script_func] };
                                if (!script_functions_dups[script_func].Contains(file)) // don't add the same file to list. Sometimes the same function call is also a comment.
                                {
                                    script_functions_dups[script_func].Add(file);
                                }
                            }
                            else
                            {
                                if (!script_functions_dups[script_func].Contains(file)) // don't add the same file to list. Sometimes the same function call is also a comment.
                                {
                                    script_functions_dups[script_func].Add(file);
                                }
                            }
                        }
                        script_functions[script_func] = file;
                    }
                    else
                    {
                        Console.WriteLine("ERR '{0}' found at index {1}.", m.Value.Trim(), m.Index);
                    }
                }
            }

            //remove "duplicates" (with only 1 file entry). TODO check if this can be removed since the regex does not find commented functions anymore
            var dups = new Dictionary<string, List<string>>();
            foreach (var item in script_functions_dups)
            {
                if (item.Value.Count > 1)
                    dups[item.Key] = item.Value;
            }
            return dups;
        }


        //Do not format this code string
        public string GetDllCheckCode(string path)
        {
            path = path.Replace(@"\\", @"\");
            return string.Format(
@"import sys, os, glob, json
import vapoursynth as vs
core = vs.get_core();
path = path = r'{0}'
plugin_dir = glob.glob(path + '/*.dll')
error = []
for dll in plugin_dir:
    try:
        core.std.LoadPlugin(path = dll)
        core.std.LoadPlugin(path = dll)
    except Exception as e:
        error.append([os.path.basename(dll), str(e)])
        continue
print(json.dumps(error))
            ", path);
        }

        public string run_python(string cmd)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = python_bin;
            start.Arguments = string.Format("-c \"{0}\"", cmd);
            start.UseShellExecute = false;// Do not use OS shell
            start.CreateNoWindow = true; // We don't need new window
            start.RedirectStandardOutput = true;// Any output, generated by application will be redirected back
            start.RedirectStandardError = false; // Any error in standard output will be redirected back (for example exceptions)
            using (Process process = Process.Start(start))
            {
                using (StreamReader reader = process.StandardOutput)
                {
                    string result = reader.ReadToEnd();
                    return result;
                }
            }
        }

        public string run_listpedeps(string file)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "listpedeps.exe";
            start.Arguments = string.Format("\"{0}\"", file);
            start.UseShellExecute = false;// Do not use OS shell
            start.CreateNoWindow = true; // We don't need new window
            start.RedirectStandardOutput = true;// Any output, generated by application will be redirected back
            start.RedirectStandardError = false; // Any error in standard output will be redirected back (for example exceptions)
            try
            {
                using (Process process = Process.Start(start))
                {
                    using (StreamReader reader = process.StandardOutput)
                    {
                        string result = reader.ReadToEnd();
                        return result;
                    }
                }
            } catch
            {
                return null;
            }
            
        }

        public class Depends
        {
            public string file;
            public string architecture;
            public string machine_name;
            public string subsystem;
            public string minimum_windows_version;
            public List<string> Imports = new List<string>();
            public List<string> Exports = new List<string>();
            public bool IsVapourSynthPlugin = false;
            public bool IsAvisynthPlugin = false;
        }
    }
}
