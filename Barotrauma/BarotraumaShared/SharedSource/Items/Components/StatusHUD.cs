using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public partial class StatusHUD : ItemComponent
    {
        public StatusHUD(Item item, XElement element)
            : base(item, element)
        {
        }
    }
}
