using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DatingApp.API.Data;
using DatingApp.API.DTOs;
using DatingApp.API.Helpers;
using DatingApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DatingApp.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/users/{userId}/photos")]
    public class PhotosController : ControllerBase
    {
        private readonly IDatingRepository _repo;
        private readonly IMapper _mapper;
        private readonly IOptions<CloudinarySettings> _cloudinaryConfig;
        private Cloudinary _cloudinary;

        public PhotosController(IDatingRepository repo,
                                IMapper mapper,
                                IOptions<CloudinarySettings> cloudinaryConfig)
        {
            _cloudinaryConfig = cloudinaryConfig;
            _mapper = mapper;
            _repo = repo;

            Account acc = new Account(
                _cloudinaryConfig.Value.CloudName,
                _cloudinaryConfig.Value.ApiKey,
                _cloudinaryConfig.Value.ApiSecret
            );
            
            _cloudinary = new Cloudinary(acc);
        }

        [HttpGet("{photoId}", Name = "GetPhoto")]
        public async Task<IActionResult> GetPhoto(int photoId)
        {
            var photoFromRepo = await _repo.GetPhoto(photoId);
            var photo = _mapper.Map<PhotoForReturnDto>(photoFromRepo);
            return Ok(photo);
        }


        [HttpPost]
        public async Task<IActionResult> AddPhotoForUser(int userId, [FromForm]PhotoForCreationDto photoForCreationDto)
        {
            if (userId != int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value))
                return Unauthorized();
            
            var userFromRepo = await _repo.GetUser(userId);
            var file = photoForCreationDto.File;
            var uploadResult = new ImageUploadResult();

            if (file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var uploadParams = new ImageUploadParams()
                    {
                        File = new FileDescription(file.Name, stream),
                        Transformation = new Transformation().Width(500).Height(500).Crop("fill").Gravity("face")
                    };

                    uploadResult = _cloudinary.Upload(uploadParams);
                }
            }

            photoForCreationDto.Url = uploadResult.Uri.ToString();
            photoForCreationDto.PublicId = uploadResult.PublicId;

            var photo = _mapper.Map<Photo>(photoForCreationDto);

            if (!userFromRepo.Photos.Any(u => u.IsMain))
            {
                photo.IsMain = true;
            }

            userFromRepo.Photos.Add(photo);

            

            if (await _repo.SaveAll())
            {
                var photoToReturn = _mapper.Map<PhotoForReturnDto>(photo);
                // return CreatedAtRoute("GetPhoto", new { controller = "Photos", photoId = photo.Id }, photoToReturn);
                return Ok(photoToReturn);
            }

            return BadRequest("Could not add the photo.");
        }

        [HttpPost("{photoId}/setMain")]
        public async Task<IActionResult> SetMainPhoto(int userId, int photoId)
        {
            if (userId != int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value))
                return Unauthorized();
            
            var userFromRepo = await _repo.GetUser(userId);
            if (!userFromRepo.Photos.Any(p => p.Id == photoId))
                return Unauthorized();
            
            var photoFromRepo = await _repo.GetPhoto(photoId);
            if (photoFromRepo.IsMain)
                return BadRequest("This is already the main photo.");
            
            var currentMainPhoto = await _repo.GetMainPhotoForUser(userId);
            currentMainPhoto.IsMain = false;
            photoFromRepo.IsMain = true;

            if (await _repo.SaveAll())
                return NoContent();

            return BadRequest("Could not set photo to main");
        }

        [HttpDelete("{photoId}")]
        public async Task<IActionResult> DeletePhoto(int userId, int photoId)
        {
            if (userId != int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value))
                return Unauthorized();
            
            var userFromRepo = await _repo.GetUser(userId);
            if (!userFromRepo.Photos.Any(p => p.Id == photoId))
                return Unauthorized();
            
            var photoFromRepo = await _repo.GetPhoto(photoId);
            if (photoFromRepo.IsMain)
                return BadRequest("You cannot delete your main photo.");

            if (photoFromRepo.PublicId == null)
            {
                _repo.Delete(photoFromRepo);
            } 
            else
            {
                var result = _cloudinary.Destroy(new DeletionParams(photoFromRepo.PublicId));
                if (result.Result == "ok") {
                    _repo.Delete(photoFromRepo);
                }
            }

            if (await _repo.SaveAll())
                return Ok();
            return BadRequest("Failed to delete the photo.");
        }

    }
}