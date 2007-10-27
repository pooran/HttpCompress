using System;
using System.IO;
using System.Web;

using System.Collections;
using System.Collections.Specialized;

namespace blowery.Web.HttpModules {
  /// <summary>
  /// An HttpModule that hooks onto the Response.Filter property of the
  /// current request and tries to compress the output, based on what
  /// the browser supports
  /// </summary>
  /// <remarks>
  /// <p>This HttpModule uses classes that inherit from <see cref="HttpCompressingFilter"/>.
  /// We already support gzip and deflate (aka zlib), if you'd like to add 
  /// support for compress (which uses LZW, which is licensed), add in another
  /// class that inherits from HttpFilter to do the work.</p>
  /// 
  /// <p>This module checks the Accept-Encoding HTTP header to determine if the
  /// client actually supports any notion of compression.  Currently, we support
  /// the deflate (zlib) and gzip compression schemes.  I chose not to implement
  /// compress, because it's uses lzw, which generally requires a license from 
  /// Unisys.  For more information about the common compression types supported,
  /// see http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.11 for details.</p> 
  /// </remarks>
  /// <seealso cref="HttpCompressingFilter"/>
  /// <seealso cref="Stream"/>
  public sealed class HttpCompressionModule : IHttpModule {

    /// <summary>
    /// Init the handler and fulfill <see cref="IHttpModule"/>
    /// </summary>
    /// <remarks>
    /// This implementation hooks the BeginRequest event on the <see cref="HttpApplication"/>.
    /// This should be fine.
    /// </remarks>
    /// <param name="context">The <see cref="HttpApplication"/> this handler is working for.</param>
    void IHttpModule.Init(HttpApplication context) {
      context.BeginRequest += new EventHandler(this.CompressContent);
    }

    /// <summary>
    /// Implementation of <see cref="IHttpModule"/>
    /// </summary>
    /// <remarks>
    /// Currently empty.  Nothing to really do, as I have no member variables.
    /// </remarks>
    void IHttpModule.Dispose() { }

    /// <summary>
    /// EventHandler that gets ahold of the current request context and attempts to compress the output.
    /// </summary>
    /// <param name="sender">The <see cref="HttpApplication"/> that is firing this event.</param>
    /// <param name="e">Arguments to the event</param>
    void CompressContent(object sender, EventArgs e) {

      HttpApplication app = (HttpApplication)sender;

      // get the accepted types of compression
      // i'm getting them all because the string[] that GetValues was returning
      // contained on member with , delimited items.  rather silly, so i just go for the whole thing now
      // also, the get call is case-insensitive, i just use the pretty casing cause it looks nice
      string[] acceptedTypes = app.Request.Headers.GetValues("Accept-Encoding");

      // this will happen if the header wasn't found.  just bail out because the client doesn't want compression
      if(acceptedTypes == null) {
        return;
      }

      // try and find a viable filter for this request
      HttpCompressingFilter filter = GetFilterForScheme(acceptedTypes, app.Response.Filter);

      // the filter will be null if no filter was found.  if no filter, just bail.
      if(filter == null) {
        app.Context.Trace.Write("HttpCompressionModule", "Cannot find filter to support any of the client's desired compression schemes");
        return;
      }

      // if we get here, we found a viable filter.
      // set the filter and change the Content-Encoding header to match so the client can decode the response
      app.Response.Filter = filter;
      app.Response.AppendHeader("Content-Encoding", filter.NameOfContentEncoding);

    }


    /// <summary>
    /// Get ahold of a <see cref="HttpCompressingFilter"/> for the given encoding scheme.
    /// If no encoding scheme can be found, it returns null.
    /// </summary>
    HttpCompressingFilter GetFilterForScheme(string[] schemes, Stream currentFilterStream) {

	  bool foundDeflate = false;
	  bool foundGZip = false;
	  bool foundStar = false;
      
      float deflateQuality = 0;
	  float gZipQuality = 0;
	  float starQuality = 0;

	  bool isAcceptableDeflate;
	  bool isAcceptableGZip;
	  bool isAcceptableStar;

      for (int i = 0; i<schemes.Length;i++) {
	    string acceptEncodingValue = schemes[i].Trim().ToLower();

	    if (acceptEncodingValue.StartsWith("deflate")) {
		  foundDeflate = true;
		  
		  float newDeflateQuality = GetQuality(acceptEncodingValue);
		  if (deflateQuality < newDeflateQuality)
		    deflateQuality = newDeflateQuality;
		}

		if (acceptEncodingValue.StartsWith("gzip") || acceptEncodingValue.StartsWith("x-gzip")) {
		  foundGZip = true;
		  
		  float newGZipQuality = GetQuality(acceptEncodingValue);
		  if (gZipQuality < newGZipQuality)
		    gZipQuality = newGZipQuality;
		}
	    
		if (acceptEncodingValue.StartsWith("*")) {
		  foundStar = true;
		  
		  float newStarQuality = GetQuality(acceptEncodingValue);
		  if (starQuality < newStarQuality)
		    starQuality = newStarQuality;
		}
      }

	  isAcceptableStar = foundStar && (starQuality > 0);
	  isAcceptableDeflate = (foundDeflate && (deflateQuality > 0)) || (!foundDeflate && isAcceptableStar);
	  isAcceptableGZip = (foundGZip && (gZipQuality > 0)) || (!foundGZip && isAcceptableStar);

	  if (isAcceptableDeflate && !foundDeflate)
	    deflateQuality = starQuality;

	  if (isAcceptableGZip && !foundGZip)
	    gZipQuality = starQuality;

	  HttpCompressionModuleSettings settings = HttpCompressionModuleSettings.GetSettings();
		
      if (isAcceptableDeflate && (!isAcceptableGZip || (deflateQuality > gZipQuality)))
	    return new DeflateFilter(currentFilterStream, settings.CompressionLevel);
	  if (isAcceptableGZip && (!isAcceptableDeflate || (deflateQuality < gZipQuality)))
	    return new GZipFilter(currentFilterStream);
	        
      // if they support the preferred algorithm, use it
      if(isAcceptableDeflate && settings.PreferredAlgorithm == CompressionTypes.Deflate)
        return new DeflateFilter(currentFilterStream, settings.CompressionLevel);
      if(isAcceptableGZip && settings.PreferredAlgorithm == CompressionTypes.GZip)
        return new GZipFilter(currentFilterStream);

      // return null.  we couldn't find a filter.
      return null;
    }
	
	float GetQuality(string acceptEncodingValue) {
	  int qParam = acceptEncodingValue.IndexOf("q=");

	  if (qParam >= 0) {
	    return float.Parse(acceptEncodingValue.Substring(qParam+2, acceptEncodingValue.Length - (qParam+2)));
	  } else 
		return 1;
	}
  }
}
