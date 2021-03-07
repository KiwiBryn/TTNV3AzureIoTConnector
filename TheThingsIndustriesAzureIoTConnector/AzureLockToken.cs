//---------------------------------------------------------------------------------
// Copyright (c) March 2022, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	public class AzureLockToken
	{
		public static readonly string AzureCorrelationPrefix = "az:LockToken:";

		public static bool TryGet(List<string> correlationIds, out string azureLockToken)
		{
			azureLockToken = string.Empty;

			// if AzureCorrelationPrefix prefix not found bug out
			if (!correlationIds.Any(o => o.StartsWith(AzureCorrelationPrefix)))
			{
				return false;
			}

			azureLockToken = correlationIds.Single(o => o.StartsWith(AzureCorrelationPrefix));

			azureLockToken = azureLockToken.Remove(0, AzureCorrelationPrefix.Length);

			return true;
		}

		public static List<string> Add(string azureLockToken)
		{
			return new List<string>()
			{
				$"{AzureCorrelationPrefix}{azureLockToken}"
			};
		}
	}
}
