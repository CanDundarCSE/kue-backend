namespace Kue.Api.Dtos.Lists;

public class CustomListDetailDto : CustomListDto
{
    public List<CustomListItemDto> Items { get; set; } = [];
}
