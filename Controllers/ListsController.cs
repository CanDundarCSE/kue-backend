using Microsoft.AspNetCore.Mvc;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/me/lists")]
public class ListsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetLists()
    {
        return Ok(new
        {
            items = new[]
            {
                new
                {
                    id = 1,
                    name = "İzlenecek Animeler",
                    description = "İzlemek istediğim animeler"
                }
            }
        });
    }

    [HttpPost]
    public IActionResult CreateList()
    {
        return Ok(new
        {
            message = "List created successfully!"
        });
    }

    [HttpGet("{listId:int}")]
    public IActionResult GetList(int listId)
    {
        return Ok(new
        {
            id = listId,
            name = "Animes to Watch",
            description = "Animes I want to watch"
        });
    }

    [HttpPut("{listId:int}")]
    public IActionResult UpdateList(int listId)
    {
        return Ok(new
        {
            id = listId,
            message = "List updated successfully!"
        });
    }

    [HttpDelete("{listId:int}")]
    public IActionResult DeleteList(int listId)
    {
        return Ok(new
        {
            id = listId,
            message = "List deleted successfully!"
        });
    }

    [HttpGet("{listId:int}/items")]
    public IActionResult GetListItems(int listId)
    {
        return Ok(new
        {
            listId,
            items = new[]
            {
                new
                {
                    mediaType = "anime",
                    externalSource = "anilist",
                    externalId = 20
                }
            }
        });
    }

    [HttpPost("{listId:int}/items")]
    public IActionResult AddListItem(int listId)
    {
        return Ok(new
        {
            listId,
            message = "Media added to list successfully!"
        });
    }

    [HttpPut("{listId:int}/items/{mediaId:int}")]
    public IActionResult UpdateListItem(int listId, int mediaId)
    {
        return Ok(new
        {
            listId,
            mediaId,
            message = "List item updated successfully!"
        });
    }

    [HttpDelete("{listId:int}/items/{mediaId:int}")]
    public IActionResult RemoveListItem(int listId, int mediaId)
    {
        return Ok(new
        {
            listId,
            mediaId,
            message = "Media removed from list successfully!"
        });
    }
}