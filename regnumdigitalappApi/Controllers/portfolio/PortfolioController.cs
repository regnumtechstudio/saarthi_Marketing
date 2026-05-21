using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace regnumdigitalappApi.Controllers.portfolio
{
    [Route("api/[controller]")]
    [ApiController]
    public class PortfolioController : ControllerBase
    {

        // GET: api/<PortfolioController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<PortfolioController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<PortfolioController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<PortfolioController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<PortfolioController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
