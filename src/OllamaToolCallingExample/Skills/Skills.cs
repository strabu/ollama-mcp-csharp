using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OllamaToolCallingExample.Skills
{
	public class Skills
	{
		//public string DefaultSkillsCatalog =
	//"*browser*: use the local webbrowser\r\n*file*: read or write files";


		public string GetSkills()
		{
			return @"**browser**: use the local webbrowser
**consent banner**: how you can close or accept privacy/consent banners
**file**: read or write files";
		}


		public string LearnSkill(string command)
		{
			if (command.Contains("browser"))
				return @"To access the browser do the following:
use run_shell and then:
1. Use 'agent-browser open <url>' to navigate. Cookie/consent dialogs are handled automatically.
2a. Run 'agent-browser screenshot [path]' to take a screenshot.
2b. Run 'agent-browser snapshot' to see the page structure and find element references (e.g., [ref=e12]).
3. Use 'agent-browser click @ref' to click elements by their reference ID from the snapshot.
4. Use 'agent-browser get text @ref' to extract text from elements.
5. IMPORTANT: After opening a page, always run 'agent-browser snapshot' to inspect the content and find what you need.

**In case of a Consent Banner pops up you must:**
1. Run: agent-browser cookies set --name _sp_su --value true --domain .derstandard.at --path /
2. Run: agent-browser cookies set --name _sp_su --value true --domain .orf.at --path /
3. Run: agent-browser cookies set --name _sp_su --value true --path /
4. Run: agent-browser reload
5. Run: agent-browser wait 3000
";

			if (command.Contains("consent"))
				return @"To pass the consent-dialog you have to do the following steps with run_shell:
1. Run: agent-browser cookies set --name _sp_su --value true --domain .derstandard.at --path /
2. Run: agent-browser cookies set --name _sp_su --value true --domain .orf.at --path /
3. Run: agent-browser cookies set --name _sp_su --value true --path /
4. Run: agent-browser reload
5. Run: agent-browser wait 3000";

			return $"Skill '{command}' not found";
		}

		internal DataContent LookAtImageFile(string imageFilePath)
		{
			if (!File.Exists(imageFilePath))
				return null;

			byte[] imageBytes = File.ReadAllBytes(imageFilePath);
			var imageContent = new DataContent(imageBytes, "image/png");

			return imageContent;
		}
	}
}
